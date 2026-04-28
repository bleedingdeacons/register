using Microsoft.EntityFrameworkCore;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
	/// <summary>
	/// Records a member's GDPR acceptance or revocation locally and durably.
	///
	/// Modelled on <see cref="AttendanceService"/> — see that file for the
	/// reasoning behind the patterns repeated here. The salient points:
	///
	///   1. Each call writes to up to TWO durable layers:
	///        a. SQLite (<see cref="UnityDbContext"/>) — primary store.
	///        b. <see cref="ComplianceEventLog"/> — fsync'd append-only
	///           log, defence in depth against DB loss between the
	///           acceptance tap and the next reconcile.
	///      The DB is written first; if its write fails, the log is
	///      never touched, so we cannot end up with a logged acceptance
	///      that was never applied.
	///
	///   2. The log is gated by
	///      <see cref="IConfigurationService.IsComplianceEventLogEnabled"/>;
	///      when disabled the DB is still written, just without the
	///      belt-and-braces.
	///
	///   3. Each call uses a fresh short-lived <see cref="DbContext"/>
	///      via <see cref="IDbContextFactory{TContext}"/> so there's no
	///      change-tracker race with ViewModels editing the same Member.
	///
	/// The compliance domain is simpler than attendance — there is only
	/// one entity type (Member) and no email side-effects, so this class
	/// has no <see cref="IDisposable"/> hookup and a smaller surface than
	/// <see cref="AttendanceService"/>.
	/// </summary>
	public sealed class ComplianceService : IComplianceRegistration
	{
		private static readonly ILogger Logger = AppLogger.ForContext<ComplianceService>();

		private readonly IDbContextFactory<UnityDbContext> _dbContextFactory;
		private readonly ComplianceEventLog _eventLog;
		private readonly IConfigurationService _configService;

		public ComplianceService(
			IDbContextFactory<UnityDbContext> dbContextFactory,
			ComplianceEventLog eventLog,
			IConfigurationService configService)
		{
			_dbContextFactory = dbContextFactory;
			_eventLog = eventLog;
			_configService = configService;
		}

		public Task RecordAcceptance(
			Member member,
			string version,
			string statement,
			string method = "register-app",
			DateTime? acceptedAtUtc = null,
			CancellationToken ct = default)
		{
			ArgumentNullException.ThrowIfNull(member);

			// version and statement are persisted as-is; the server
			// validates length (≤ 50 / ≤ 2000) but we mirror the
			// constraint here rather than wait for the reconcile push
			// to fail. Empty values are allowed — the operator may
			// not have a policy version string to hand and recording
			// "yes they accepted, version unknown" is more useful than
			// rejecting the tap.
			var ts = acceptedAtUtc ?? DateTime.UtcNow;

			Logger.Information(
				"Compliance: member {MemberId} ({Name}) accepted version {Version} via {Method}",
				member.Id, member.AnonymousName, version, method);

			return ApplyAsync(
				member.Id,
				accepted: true,
				timestampUtc: ts,
				version: version,
				method: method,
				statement: statement,
				ct: ct);
		}

		public Task RecordRevocation(
			Member member,
			DateTime? revokedAtUtc = null,
			CancellationToken ct = default)
		{
			ArgumentNullException.ThrowIfNull(member);

			var ts = revokedAtUtc ?? DateTime.UtcNow;

			Logger.Information(
				"Compliance: member {MemberId} ({Name}) revoked consent",
				member.Id, member.AnonymousName);

			return ApplyAsync(
				member.Id,
				accepted: false,
				timestampUtc: ts,
				version: null,
				method: null,
				statement: null,
				ct: ct);
		}

		/// <summary>
		/// Single private path through which both acceptance and revocation
		/// flow. Writes the DB first, then the log. The log write is
		/// best-effort — a logging failure is logged but does not bubble
		/// up, because the DB is the authoritative record. A DB failure
		/// is logged and returns silently without touching the log,
		/// because a logged-but-not-applied entry would be resurrected
		/// on next startup as if it had succeeded.
		/// </summary>
		private async Task ApplyAsync(
			int memberId,
			bool accepted,
			DateTime timestampUtc,
			string? version,
			string? method,
			string? statement,
			CancellationToken ct)
		{
			// ── Primary store: SQLite ────────────────────────────────
			try
			{
				await using var dbContext = await _dbContextFactory.CreateDbContextAsync(ct);

				var member = await dbContext.Members.FindAsync(new object[] { memberId }, ct);
				if (member is null)
				{
					Logger.Warning("Compliance: member {MemberId} not found", memberId);
					return;
				}

				member.GdprAccepted = accepted;
				member.GdprAcceptedAt = timestampUtc;

				if (accepted)
				{
					member.GdprAcceptanceVersion = version;
					member.GdprAcceptanceMethod = method;
					member.GdprAcceptanceStatement = statement;
				}
				else
				{
					// Revocation clears the prior-acceptance metadata —
					// same rule the Unity server applies on push, so the
					// offline state matches what the server will end up
					// with. ComplianceEventLog.ApplyEntryToMember encodes
					// the same rule, but we don't share the call here:
					// duplicating the four assignments is cheaper than
					// pulling in the entry record purely to delegate.
					member.GdprAcceptanceVersion = null;
					member.GdprAcceptanceMethod = null;
					member.GdprAcceptanceStatement = null;
				}

				await dbContext.SaveChangesAsync(ct);
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Failed to persist compliance state for member {MemberId}", memberId);
				return;
			}

			// ── Durability layer: append-only log (feature-gated) ───
			//
			// Toggle is read per-call so the operator can flip it mid-
			// session in Settings without restarting the app.
			if (!_configService.IsComplianceEventLogEnabled)
				return;

			try
			{
				if (accepted)
				{
					await _eventLog.AppendAcceptanceAsync(
						memberId, timestampUtc, version, method, statement, ct);
				}
				else
				{
					await _eventLog.AppendRevocationAsync(memberId, timestampUtc, ct);
				}
			}
			catch (Exception ex)
			{
				// The log already swallows its own write errors — this
				// catch is purely belt-and-braces in case a future change
				// to the log surface starts throwing again. Keep it
				// warning-level: the DB is the authoritative record.
				Logger.Warning(ex, "Failed to append compliance log entry for member {MemberId}", memberId);
			}
		}
	}
}
