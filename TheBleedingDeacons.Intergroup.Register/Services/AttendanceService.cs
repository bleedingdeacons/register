using Microsoft.EntityFrameworkCore;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
	/// <summary>
	/// Manages attendance registration state locally.
	///
	/// Each registration is written to up to TWO durable layers:
	///
	///   1. The SQLite database (<see cref="UnityDbContext"/>) — primary store,
	///      read by reconciliation's snapshot diff.
	///
	///   2. The <see cref="RegistrationEventLog"/> — crash-durable fsync'd
	///      append-only log, used to rebuild the DB if it's lost or corrupted
	///      between a registration and the end-of-meeting reconcile.
	///      Gated by <see cref="IConfigurationService.IsRegistrationEventLogEnabled"/>;
	///      when disabled the DB is still written, just without the belt-and-braces.
	///
	/// The DB is written first. If the DB write succeeds but the log write
	/// fails, the registration is still safe — the log is defence in depth,
	/// not the primary record. If the DB write fails, the log is never
	/// touched, so we can't end up with a logged registration that was
	/// never actually applied.
	///
	/// <b>Context lifetime</b>: each register/unregister creates its own
	/// short-lived DbContext via <see cref="IDbContextFactory{TContext}"/>,
	/// then disposes it. This removes the race hazard of sharing a scoped
	/// context with ViewModels that also write to the same tables.
	/// </summary>
	public class AttendanceService : IAttendanceRegistration<Position>, IAttendanceRegistration<Group>, IDisposable
	{
		private static readonly ILogger Logger = AppLogger.ForContext<AttendanceService>();

		private readonly IEmailService _emailService;
		private readonly IEmailTemplateService _emailTemplate;
		private readonly IDbContextFactory<UnityDbContext> _dbContextFactory;
		private readonly RegistrationEventLog _eventLog;
		private readonly IConfigurationService _configService;

		private readonly EventHandler<EmailSentEventArgs> _emailSentHandler;
		private readonly EventHandler<EmailFailedEventArgs> _emailFailedHandler;
		private bool _disposed;

		public AttendanceService(
			IEmailTemplateService emailTemplate,
			IEmailService emailService,
			IDbContextFactory<UnityDbContext> dbContextFactory,
			RegistrationEventLog eventLog,
			IConfigurationService configService)
		{
			_emailService = emailService;
			_emailTemplate = emailTemplate;
			_dbContextFactory = dbContextFactory;
			_eventLog = eventLog;
			_configService = configService;

			_emailSentHandler = (s, e) => Logger.Information("Email sent to {Recipient}", e.Email.To);
			_emailFailedHandler = (s, e) => Logger.Warning("Email failed for {Recipient}: {Error}", e.Email.To, e.Error);

			_emailService.EmailSent += _emailSentHandler;
			_emailService.EmailFailed += _emailFailedHandler;
		}

		public async Task Register(Position entity)
		{
			Logger.Information("Position {PositionName} attendance registered locally", entity.ShortDescription);
			await SetPositionRegisteredAsync(entity.Id, true);
		}

		public async Task Register(Group entity)
		{
			Logger.Information("Group {GroupName} attendance registered locally (Proxy={Proxy})", entity.Name, entity.GsrProxy);
			await SetGroupRegisteredAsync(entity.Id, true, entity.GsrProxy, entity.GsrProxyName);
		}

		public async Task Unregister(Position entity)
		{
			Logger.Information("Position {PositionName} attendance unregistered locally", entity.ShortDescription);
			await SetPositionRegisteredAsync(entity.Id, false);
		}

		public async Task Unregister(Group entity)
		{
			Logger.Information("Group {GroupName} attendance unregistered locally", entity.Name);
			await SetGroupRegisteredAsync(entity.Id, false);
		}

		private async Task SetGroupRegisteredAsync(
			int groupId,
			bool registered,
			bool gsrProxy = false,
			string? gsrProxyName = null,
			CancellationToken ct = default)
		{
			// IDs of positions that were also flipped to Registered=true as a
			// side effect of this group registration. Captured inside the DB
			// transaction and used outside it to append to the event log.
			// Empty when the toggle is off, when we're unregistering, or when
			// no member of this group holds a position.
			var cascadedPositionIds = new List<int>();

			// ── Primary store: SQLite ────────────────────────────────
			try
			{
				// Fresh context per call — no shared state with other
				// services or ViewModels, so no risk of stale-entity
				// overwrites via EF's change tracker.
				await using var dbContext = await _dbContextFactory.CreateDbContextAsync(ct);

				var group = await dbContext.Groups.FindAsync(new object[] { groupId }, ct);
				if (group is null)
				{
					Logger.Warning("SetGroupRegisteredAsync: group {GroupId} not found", groupId);
					return;
				}

				group.Registered = registered;
				group.GsrProxy = gsrProxy;
				group.GsrProxyName = gsrProxy ? gsrProxyName : null;

				// ── Cascade: also register positions held by this group's members ──
				//
				// Only on the register path (registered=true). The un-register
				// path intentionally does NOT cascade: a position holder may
				// have been deliberately registered separately, may hold their
				// position through a different group's registration, or may
				// still be in attendance even if their home group steps out.
				// Silently flipping their row off would erase intent we can't
				// reconstruct.
				//
				// We look up candidate position IDs from the live DB (not from
				// the passed-in entity graph) so the decision is based on the
				// canonical persisted membership, not whatever the ViewModel
				// happened to hand us. Idempotent: positions already flagged
				// Registered stay that way, and we don't log duplicate entries
				// for them.
				if (registered && _configService.IsAutoRegisterPositionsOnGroupEnabled)
				{
					var candidateIds = await dbContext.Members
						.Where(m => m.HomeGroupId == groupId && m.IntergroupPositionId != null)
						.Select(m => m.IntergroupPositionId!.Value)
						.Distinct()
						.ToListAsync(ct);

					if (candidateIds.Count > 0)
					{
						var positionsToFlip = await dbContext.Positions
							.Where(p => candidateIds.Contains(p.Id) && !p.Registered)
							.ToListAsync(ct);

						foreach (var position in positionsToFlip)
						{
							position.Registered = true;
							cascadedPositionIds.Add(position.Id);
						}

						if (positionsToFlip.Count > 0)
						{
							Logger.Information(
								"Auto-registered {Count} position(s) {PositionIds} as cascade from group {GroupId}",
								positionsToFlip.Count, cascadedPositionIds, groupId);
						}
					}
				}

				await dbContext.SaveChangesAsync(ct);
			}
			catch (Exception ex)
			{
				// DB write failed — do NOT write to the log. A log entry
				// without a corresponding DB state would be resurrected on
				// next startup as if the registration had happened.
				Logger.Warning(ex, "Failed to persist Registered state for group {GroupId}", groupId);
				return;
			}

			// ── Durability layer: append-only log (feature-gated) ───
			// Toggle is checked per-call so ops can flip it mid-session
			// without restart. Reads are cheap — Preferences lookup is
			// a dictionary hit.
			if (_configService.IsRegistrationEventLogEnabled)
			{
				await _eventLog.AppendGroupAsync(groupId, registered, gsrProxy, gsrProxyName, ct);

				// Mirror the position flips into the event log so a replay
				// after a crash rebuilds the same state. A failure here is
				// warned-only — the DB write is the authoritative record
				// and has already succeeded; the log is defence in depth.
				foreach (var positionId in cascadedPositionIds)
				{
					try
					{
						await _eventLog.AppendPositionAsync(positionId, true, ct);
					}
					catch (Exception ex)
					{
						Logger.Warning(
							ex,
							"Failed to append cascaded position {PositionId} to event log",
							positionId);
					}
				}
			}
		}

		private async Task SetPositionRegisteredAsync(int positionId, bool registered, CancellationToken ct = default)
		{
			// ── Primary store: SQLite ────────────────────────────────
			try
			{
				await using var dbContext = await _dbContextFactory.CreateDbContextAsync(ct);

				var position = await dbContext.Positions.FindAsync(new object[] { positionId }, ct);
				if (position is null)
				{
					Logger.Warning("SetPositionRegisteredAsync: position {PositionId} not found", positionId);
					return;
				}

				position.Registered = registered;
				await dbContext.SaveChangesAsync(ct);
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Failed to persist Registered state for position {PositionId}", positionId);
				return;
			}

			// ── Durability layer: append-only log (feature-gated) ───
			if (_configService.IsRegistrationEventLogEnabled)
			{
				await _eventLog.AppendPositionAsync(positionId, registered, ct);
			}
		}

		public void Dispose()
		{
			if (!_disposed)
			{
				_emailService.EmailSent -= _emailSentHandler;
				_emailService.EmailFailed -= _emailFailedHandler;
				_disposed = true;
			}
		}
	}
}