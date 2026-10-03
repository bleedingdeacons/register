using System;

namespace TheBleedingDeacons.Intergroup.Register.Models
{
	/// <summary>
	/// View-model populated per-recipient and rendered into
	/// <c>Templates/ComplianceAcceptanceEmail.html</c> after a member taps
	/// Accept on the GDPR consent popup. Mirrors the role
	/// <see cref="WelcomeEmail"/> plays for registration: a flat bag of
	/// strings that the placeholder-based template service can substitute
	/// without any reflection-on-reflection or null-handling on the
	/// template author's side.
	///
	/// <para>Two sources feed this model:</para>
	/// <list type="bullet">
	/// <item>The <c>Member</c> being processed — supplies the recipient's
	/// display name and the wording-of-the-day they actually accepted
	/// (<c>GdprAcceptanceStatement</c>) along with the timestamp and
	/// the capture method.</item>
	/// <item>The cached active <see cref="PrivacyPolicy"/> — supplies the
	/// audit-trail metadata that doesn't live on the Member row: policy
	/// id, title, version and the upstream-modified timestamp. These
	/// are the bits a regulator or the member themselves would want as
	/// proof of "what was the policy at the moment of acceptance".</item>
	/// </list>
	///
	/// <para>All fields are <see cref="string"/> rather than typed
	/// (DateTime, int, etc.) because <see cref="Services.EmailTemplateService"/>
	/// substitutes via <c>ToString()</c> without any culture or format
	/// awareness; pre-formatting at the call-site keeps the template
	/// readable and the rendered output predictable across locales.</para>
	/// </summary>
	public class ComplianceEmail
	{
		// ── Recipient ─────────────────────────────────────────────────

		/// <summary>
		/// The member's anonymous-name display value. Used for the
		/// greeting line and as part of the audit trail in the email
		/// body. Never null — the sender substitutes <c>"there"</c>
		/// when the member has no name on file, mirroring
		/// <c>AttendanceService.ExtractFirstName</c>'s behaviour.
		/// </summary>
		public string AnonymousName { get; set; } = string.Empty;

		/// <summary>
		/// The recipient's email address — duplicated into the body so the
		/// member can confirm at a glance which address the record is
		/// associated with. Not used for routing; that's done by the
		/// surrounding <see cref="Services.Interfaces.IEmailService"/> call.
		/// </summary>
		public string RecipientEmail { get; set; } = string.Empty;

		// ── Acceptance event ──────────────────────────────────────────

		/// <summary>
		/// Pre-formatted UTC timestamp of the moment the member tapped
		/// Accept (e.g. <c>"2026-05-03 18:42 UTC"</c>). Formatted at
		/// the sender so the rendered email is locale-stable.
		/// </summary>
		public string AcceptedAtUtc { get; set; } = string.Empty;

		/// <summary>
		/// How acceptance was captured — passed through verbatim from
		/// <see cref="Services.Interfaces.IComplianceRegistration.RecordAcceptance"/>
		/// (e.g. <c>"register-app"</c>, <c>"web-form"</c>). Surfaces in
		/// the email body so the audit trail isn't only in the server
		/// log.
		/// </summary>
		public string AcceptanceMethod { get; set; } = string.Empty;

		// ── Active policy snapshot ────────────────────────────────────

		/// <summary>
		/// The post id of the active policy at the moment of acceptance
		/// (rendered as a string for template substitution). Stable
		/// across edits to the policy text but not across re-imports of
		/// the upstream WordPress site.
		/// </summary>
		public string PolicyId { get; set; } = string.Empty;

		/// <summary>
		/// The policy's display title, e.g. <c>"Privacy Policy"</c>,
		/// from the cached active policy.
		/// </summary>
		public string PolicyTitle { get; set; } = string.Empty;

		/// <summary>
		/// The policy version stamp recorded against this acceptance
		/// (e.g. <c>"2.1"</c>). The same value persisted to
		/// <c>Member.GdprAcceptanceVersion</c>.
		/// </summary>
		public string PolicyVersion { get; set; } = string.Empty;

		/// <summary>
		/// ISO-8601 timestamp of the policy's last modification on the
		/// server. Helpful in the audit trail to disambiguate two
		/// acceptances against the "same" version when wording was
		/// tweaked between them.
		/// </summary>
		public string PolicyModified { get; set; } = string.Empty;

		// ── The wording the member actually saw ───────────────────────

		/// <summary>
		/// HTML-ready rendering of the active policy body at the moment
		/// of acceptance, sourced from <see cref="CachedPrivacyPolicy.Policy"/>.
		/// The same value is also persisted to
		/// <c>Member.GdprAcceptanceStatement</c> for the local audit trail
		/// — DB row, durability log, and email body all derive from the
		/// cache, so the audit trail is internally consistent.
		///
		/// <para>The sender wraps plain-text statements in <c>&lt;p&gt;</c>
		/// blocks (HTML-encoded) so paragraphs render in HTML mail
		/// clients; statements that already contain HTML are passed
		/// through untouched. Same rule as
		/// <see cref="WelcomeEmail.Policy"/>. Empty when no policy has
		/// ever been cached on the device.</para>
		/// </summary>
		public string PolicyStatementHtml { get; set; } = string.Empty;
	}
}
