using Microsoft.EntityFrameworkCore;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Models;
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

			// Queue welcome emails for the position's holders. Runs after
			// the DB write so a delivery problem can never invalidate a
			// completed registration. The helper queues rather than sends
			// inline so the user's countdown popup isn't blocked on SMTP
			// I/O, and any per-recipient failure is logged but doesn't
			// stop the rest. This is the direct-position-registration
			// path; cascaded-from-group emails are handled inside
			// Register(Group) where we can dedupe across GSRs and holders.
			await TryQueueWelcomeEmailsAsync(entity.Holders, entity.ShortDescription);
		}

		public async Task Register(Group entity)
		{
			Logger.Information("Group {GroupName} attendance registered locally (Proxy={Proxy})", entity.Name, entity.GsrProxy);
			var cascadedPositionIds = await SetGroupRegisteredAsync(entity.Id, true, entity.GsrProxy, entity.GsrProxyName);

			// Welcome-email recipients are the active GSRs (the people
			// being directly registered) plus any members whose held
			// position was auto-registered as a cascade — they were also
			// just flipped to attending and deserve the same confirmation.
			// Deduped by Member.Id so a GSR who also holds a cascaded
			// position only gets one email, not two.
			//
			// All recipients are already on entity.Members (the cascade
			// only reaches positions held by *this* group's members), so
			// no second DB round-trip is needed to resolve them.
			var recipients = CollectGroupWelcomeRecipients(entity, cascadedPositionIds);
			await TryQueueWelcomeEmailsAsync(recipients, entity.Name);
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

		private async Task<IReadOnlyList<int>> SetGroupRegisteredAsync(
			int groupId,
			bool registered,
			bool gsrProxy = false,
			string? gsrProxyName = null,
			CancellationToken ct = default)
		{
			// IDs of positions that were also flipped to Registered=true as a
			// side effect of this group registration. Captured inside the DB
			// transaction and used outside it to append to the event log,
			// then returned to the caller so welcome-email dispatch can
			// dedupe across GSRs and cascaded position holders without
			// re-querying. Empty when the toggle is off, when we're
			// unregistering, or when no member of this group holds a position.
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
					return cascadedPositionIds;
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
				return cascadedPositionIds;
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

			return cascadedPositionIds;
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

		// ─────────────────────────────────────────────────────────────
		// Welcome-email dispatch
		// ─────────────────────────────────────────────────────────────
		//
		// A registration is "done" the moment SetGroup/PositionRegisteredAsync
		// returns successfully. Welcome emails are a follow-on — useful, but
		// strictly secondary. The helpers below therefore:
		//
		//   • Queue, never send inline. The user's countdown popup runs on
		//     the UI thread; SMTP I/O on that thread would freeze it. The
		//     queue's existing retry / circuit-breaker / offline-mode
		//     machinery owns delivery from here.
		//
		//   • Swallow their own exceptions. A welcome-email failure must
		//     never propagate up into Register() and cause the registration
		//     to look like it failed. The DB row already says Registered=true.
		//
		//   • Skip silently when a recipient has no email on file. The
		//     verify-page gate accepts a member with just a phone number,
		//     so a missing PersonalEmail is a normal and expected case,
		//     not an error.

		/// <summary>
		/// Queue a welcome email for each member in <paramref name="members"/>
		/// who has a non-empty <c>PersonalEmail</c>. Members without an email
		/// on file are skipped silently — phone-only contact is permitted by
		/// the registration gate. Per-recipient failures are logged and
		/// swallowed so a bad address can't block the rest of the batch or
		/// the registration that triggered them.
		/// </summary>
		/// <param name="members">
		/// The set of recipients. Already deduped by the caller for the
		/// group path; for the position path it's whatever holders were on
		/// the entity. Null members and members with no email are skipped.
		/// </param>
		/// <param name="meetingName">
		/// Display name of the thing being registered (group name or
		/// position short description). Used both in the email subject and
		/// in the rendered template's MeetingName field.
		/// </param>
		private async Task TryQueueWelcomeEmailsAsync(
			IEnumerable<Member>? members,
			string meetingName)
		{
			if (members is null) return;

			// Feature gate. Read inside the helper rather than at each
			// call-site so the toggle covers both the Position and Group
			// paths from a single check, and so flipping the toggle in
			// Settings takes effect on the very next registration without
			// any other code being aware of it. Off by default — fresh
			// installs do not surprise people with email until an operator
			// has explicitly opted in.
			if (!_configService.IsWelcomeEmailOnRegistrationEnabled)
			{
				Logger.Debug(
					"Welcome-email toggle is off; skipping send for {MeetingName}",
					meetingName);
				return;
			}

			foreach (var member in members)
			{
				if (member is null) continue;

				// Email is optional at the gate — only emit when present.
				var to = member.PersonalEmail?.Trim();
				if (string.IsNullOrEmpty(to)) continue;

				try
				{
					// Per-recipient model: the Policy slot is populated
					// from the member's own GdprAcceptanceStatement so
					// the email contains the exact wording they accepted,
					// not a freshly-loaded copy that may have been edited
					// since. Plain-text statements are wrapped to <p>
					// blocks so newlines render as paragraphs in the HTML
					// email; statements that already contain HTML markup
					// pass through untouched.
					var firstName = ExtractFirstName(member.AnonymousName);
					var policyHtml = FormatPolicyAsHtml(member.GdprAcceptanceStatement);

					var model = new WelcomeEmail
					{
						FirstName = firstName,
						MeetingName = meetingName,
						Email = to,
						Mobile = member.MobileNumber ?? string.Empty,
						Policy = policyHtml,
						// Fields the current template doesn't surface but
						// the model carries — populate with safe defaults
						// so a future template change doesn't leave them
						// rendering as "{{Location}}" etc.
						Location = string.Empty,
						Address = string.Empty,
						StartTime = string.Empty,
						MeetingContacts = new List<MeetingContact>()
					};

					var body = await _emailTemplate.RenderTemplateAsync("WelcomeEmail", model);
					var subject = $"Registered: {meetingName}";

					await _emailService.QueueEmailAsync(to, subject, body, isHtml: true);

					Logger.Information(
						"Queued welcome email for member {MemberId} ({Name}) to {Email} for {MeetingName}",
						member.Id, member.AnonymousName, to, meetingName);
				}
				catch (Exception ex)
				{
					// Per-recipient failures are warnings, not errors —
					// the registration is the contract; the email is
					// best-effort. Continue to the next recipient.
					Logger.Warning(ex,
						"Failed to queue welcome email for member {MemberId} ({Name}) to {Email}",
						member.Id, member.AnonymousName, to);
				}
			}
		}

		/// <summary>
		/// Build the deduped recipient list for a group registration:
		/// active GSRs unioned with members whose held position was
		/// auto-registered as a cascade. Deduping is by <c>Member.Id</c>
		/// so a GSR who also holds a cascaded position is emailed once,
		/// not twice.
		///
		/// All recipients come from the in-memory <c>group.Members</c>
		/// graph — the cascade only flips positions held by *this group's*
		/// members, so no extra DB round-trip is needed to resolve them.
		/// </summary>
		private static List<Member> CollectGroupWelcomeRecipients(
			Group group,
			IReadOnlyList<int> cascadedPositionIds)
		{
			if (group.Members is null) return new List<Member>();

			// Fast path: no cascade. Just GSRs.
			if (cascadedPositionIds.Count == 0)
			{
				return group.Members.Where(m => m.IsGsr).ToList();
			}

			// Cascade path: GSRs ∪ holders of cascaded positions, dedup by Id.
			// HashSet lookup keeps the position-membership test O(1) per
			// member regardless of how many positions cascaded.
			var cascadedSet = new HashSet<int>(cascadedPositionIds);

			return group.Members
				.Where(m =>
					m.IsGsr ||
					(m.IntergroupPositionId.HasValue &&
					 cascadedSet.Contains(m.IntergroupPositionId.Value)))
				.GroupBy(m => m.Id)
				.Select(g => g.First())
				.ToList();
		}

		/// <summary>
		/// Pull the first whitespace-separated token out of the member's
		/// display name for the <c>{{FirstName}}</c> placeholder. Falls
		/// back to "there" so the greeting is never empty (e.g. when a
		/// member was registered with only a phone number and no name).
		/// </summary>
		private static string ExtractFirstName(string? anonymousName)
		{
			if (string.IsNullOrWhiteSpace(anonymousName)) return "there";
			var trimmed = anonymousName.Trim();
			var space = trimmed.IndexOf(' ');
			return space < 0 ? trimmed : trimmed[..space];
		}

		/// <summary>
		/// Wrap a plain-text policy statement so it renders sensibly inside
		/// the HTML email template. Each paragraph (separated by blank
		/// lines) becomes a <c>&lt;p&gt;</c>; single newlines inside a
		/// paragraph become <c>&lt;br&gt;</c>. If the input already looks
		/// like HTML (contains an angle-bracketed tag) we pass it through
		/// untouched so authored HTML statements aren't double-escaped.
		/// Returns an empty string when the input is null/blank, so the
		/// template renders cleanly with no policy section instead of a
		/// stray "{{Policy}}" placeholder.
		/// </summary>
		private static string FormatPolicyAsHtml(string? statement)
		{
			if (string.IsNullOrWhiteSpace(statement)) return string.Empty;

			// Heuristic: if it already contains a tag we trust the author.
			// Avoids double-wrapping cases where a statement was authored
			// in HTML to begin with.
			if (statement.Contains('<') && statement.Contains('>'))
				return statement;

			// HTML-encode first so any stray <, >, & in plain text don't
			// break the email's HTML structure or open injection holes
			// when the rendered body is loaded by a mail client.
			var encoded = System.Net.WebUtility.HtmlEncode(statement);

			// Normalise CRLF/CR to LF, then split paragraphs on blank lines
			// and intra-paragraph newlines on single LFs.
			var normalised = encoded.Replace("\r\n", "\n").Replace('\r', '\n');
			var paragraphs = normalised.Split(
				new[] { "\n\n" },
				StringSplitOptions.RemoveEmptyEntries);

			return string.Concat(paragraphs.Select(p =>
				$"<p>{p.Trim().Replace("\n", "<br>")}</p>"));
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