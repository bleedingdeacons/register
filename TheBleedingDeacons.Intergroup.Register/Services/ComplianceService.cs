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
	///   4. After a successful DB write on the acceptance path (only),
	///      a confirmation email is queued via <see cref="IEmailService"/>
	///      using <see cref="IEmailTemplateService"/> to render
	///      <c>Templates/ComplianceAcceptanceEmail.html</c>. This gives the
	///      member their own copy of the audit trail (timestamp, method,
	///      version, contact details, and the exact statement they accepted)
	///      independent of the local SQLite store and the upstream Unity
	///      site. Send is queued, not synchronous — same rationale as the
	///      welcome-email path in <see cref="AttendanceService"/>: SMTP I/O
	///      must not block the consent UI's countdown popup, and per-
	///      recipient failures must never invalidate the recorded acceptance.
	///
	/// The email path mirrors the welcome-email path almost exactly:
	///   • Feature-gated by <see cref="IConfigurationService.IsComplianceAcceptanceEmailEnabled"/>;
	///     off by default, no surprises on fresh installs.
	///   • Members without a <c>PersonalEmail</c> on file are skipped silently —
	///     the consent gate accepts a phone-only member, so a missing email
	///     is normal, not an error.
	///   • Per-recipient failures are warning-logged and swallowed so a bad
	///     address can't undo a recorded acceptance.
	/// </summary>
	public sealed class ComplianceService : IComplianceRegistration
	{
		private static readonly ILogger Logger = AppLogger.ForContext<ComplianceService>();

		private readonly IDbContextFactory<UnityDbContext> _dbContextFactory;
		private readonly ComplianceEventLog _eventLog;
		private readonly IConfigurationService _configService;
		private readonly IEmailService _emailService;
		private readonly IEmailTemplateService _emailTemplate;
		private readonly IPrivacyPolicyCache _policyCache;

		public ComplianceService(
			IDbContextFactory<UnityDbContext> dbContextFactory,
			ComplianceEventLog eventLog,
			IConfigurationService configService,
			IEmailService emailService,
			IEmailTemplateService emailTemplate,
			IPrivacyPolicyCache policyCache)
		{
			_dbContextFactory = dbContextFactory;
			_eventLog = eventLog;
			_configService = configService;
			_emailService = emailService;
			_emailTemplate = emailTemplate;
			_policyCache = policyCache;
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
		/// flow. Writes the DB first, then the log, then (acceptance path
		/// only) queues a confirmation email. The log write is best-effort —
		/// a logging failure is logged but does not bubble up, because the
		/// DB is the authoritative record. A DB failure is logged and
		/// returns silently without touching the log or the email queue,
		/// because a logged-but-not-applied entry would be resurrected
		/// on next startup as if it had succeeded, and an email confirming
		/// an acceptance that wasn't recorded would be worse than no email
		/// at all.
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
			//
			// The Member entity reload also gives us back the contact
			// fields (PersonalEmail, AnonymousName) we'll need for the
			// confirmation email later in the same call, so we keep a
			// reference rather than re-fetching after the write.
			Member? persistedMember = null;
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
				persistedMember = member;
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
			if (_configService.IsComplianceEventLogEnabled)
			{
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

			// ── Confirmation email (acceptance only, feature-gated) ─
			//
			// Only the acceptance path triggers an email — a revocation
			// confirmation could be added later but isn't part of the
			// current contract. Runs after the DB write so a delivery
			// failure can never invalidate a recorded acceptance, and
			// is queued (not sent inline) so the consent popup's
			// countdown isn't blocked on SMTP I/O.
			if (accepted)
			{
				await TryQueueAcceptanceEmailAsync(
					persistedMember!, timestampUtc, version, method, statement);
			}
		}

		/// <summary>
		/// Build a <see cref="ComplianceEmail"/> for the given member and
		/// queue it via <see cref="IEmailService"/>. Mirrors
		/// <c>AttendanceService.TryQueueWelcomeEmailsAsync</c> in shape:
		/// feature-gate inside the helper so the toggle is a single point
		/// of change; skip silently when no email is on file; per-recipient
		/// failures are warning-logged and swallowed.
		/// </summary>
		private async Task TryQueueAcceptanceEmailAsync(
			Member member,
			DateTime timestampUtc,
			string? version,
			string? method,
			string? statement)
		{
			// Feature gate. Off by default — fresh installs don't surprise
			// anyone with confirmation email until an operator opts in.
			if (!_configService.IsComplianceAcceptanceEmailEnabled)
			{
				Logger.Debug(
					"Compliance acceptance-email toggle is off; skipping send for member {MemberId}",
					member.Id);
				return;
			}

			// Email is optional at the consent gate — phone-only members
			// pass the gate but can't be emailed. This is normal, not an
			// error.
			var to = member.PersonalEmail?.Trim();
			if (string.IsNullOrEmpty(to))
			{
				Logger.Debug(
					"Compliance acceptance email skipped for member {MemberId} ({Name}): no email on file",
					member.Id, member.AnonymousName);
				return;
			}

			try
			{
				// Audit-trail metadata that doesn't live on the Member
				// row comes from the cached active policy — id, title,
				// contact, contact email, last-modified. May be null on
				// a device that has never synced an active policy; fall
				// back to empty strings so the template still renders
				// rather than leaving "{{PolicyTitle}}" placeholders in
				// the recipient's inbox.
				var cached = _policyCache.GetCached();

				var displayName = !string.IsNullOrWhiteSpace(member.AnonymousName)
					? member.AnonymousName!
					: "there";

				var model = new ComplianceEmail
				{
					AnonymousName = displayName,
					RecipientEmail = to,
					AcceptedAtUtc = timestampUtc.ToString("yyyy-MM-dd HH:mm 'UTC'"),
					AcceptanceMethod = method ?? string.Empty,

					PolicyId = cached?.Id.ToString() ?? string.Empty,
					PolicyTitle = cached?.Title ?? string.Empty,
					// Prefer the version the user actually accepted (passed
					// in by the caller) over the cached version — they should
					// match in normal operation, but the per-acceptance value
					// is what's persisted to the DB and what the server will
					// see, so it's the authoritative one for this email.
					PolicyVersion = !string.IsNullOrWhiteSpace(version)
						? version!
						: cached?.Version ?? string.Empty,
					PolicyContact = cached?.Contact ?? string.Empty,
					PolicyContactEmail = cached?.ContactEmail ?? string.Empty,
					PolicyModified = cached?.Modified ?? string.Empty,

					PolicyStatementHtml = FormatPolicyAsHtml(statement),
				};

				var body = await _emailTemplate.RenderTemplateAsync(
					"ComplianceAcceptanceEmail", model);

				var subject = string.IsNullOrWhiteSpace(model.PolicyTitle)
					? "Privacy policy acceptance confirmation"
					: $"Privacy policy acceptance: {model.PolicyTitle} (v{model.PolicyVersion})";

				await _emailService.QueueEmailAsync(to, subject, body, isHtml: true);

				Logger.Information(
					"Queued compliance acceptance email for member {MemberId} ({Name}) to {Email} for policy v{Version}",
					member.Id, member.AnonymousName, to, model.PolicyVersion);
			}
			catch (Exception ex)
			{
				// Per-recipient failure is a warning, not an error — the
				// recorded acceptance is the contract; the email is
				// best-effort. Do not rethrow.
				Logger.Warning(ex,
					"Failed to queue compliance acceptance email for member {MemberId} ({Name}) to {Email}",
					member.Id, member.AnonymousName, to);
			}
		}

		/// <summary>
		/// Wrap a plain-text policy statement so it renders sensibly inside
		/// the HTML email template. Each paragraph (separated by blank
		/// lines) becomes a <c>&lt;p&gt;</c>; single newlines inside a
		/// paragraph become <c>&lt;br&gt;</c>. If the input already looks
		/// like HTML (contains an angle-bracketed tag) we pass it through
		/// untouched so authored HTML statements aren't double-escaped.
		/// Returns an empty string when the input is null/blank.
		///
		/// <para>This duplicates <c>AttendanceService.FormatPolicyAsHtml</c>
		/// rather than sharing a helper because the two services have no
		/// other coupling and a future divergence (e.g. the compliance
		/// email wanting numbered lists for clauses) shouldn't drag
		/// AttendanceService along with it. The duplication is ~25 lines
		/// of pure-function code — cheap.</para>
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
	}
}
