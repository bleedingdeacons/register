using Serilog;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Intergroup.Register.Services;

/// <summary>How a <see cref="ConsentRound"/> ended.</summary>
public enum ConsentOutcome
{
	/// <summary>Every member accepted. The registration may go ahead.</summary>
	Consented,

	/// <summary>A member declined. Nobody after them was asked, and the registration stops.</summary>
	Declined,

	/// <summary>There is no cached privacy policy, so nobody was asked.</summary>
	NoPolicy,

	/// <summary>The cached policy has no body text, so nobody was asked.</summary>
	EmptyPolicy,
}

/// <summary>
/// Asks each member in turn to accept the cached privacy policy, recording
/// each acceptance as it is given.
///
/// <para>Moved out of the two Verify view models, which each carried a copy.
/// What stayed there is the asking itself — the popup is MAUI — handed in
/// as a function, and the words shown when the round refuses to start.</para>
///
/// <para><b>It refuses to start without a policy worth agreeing to.</b> No
/// cached policy means the sync-stage gate was bypassed, or a sync found no
/// active policy and cleared the cache; an acceptance recorded with no
/// version would corrupt the audit trail. A policy with no body text would
/// put an empty popup with an "I Agree" button in front of a member — the
/// worst audit outcome there is, agreement to nothing. Both are refused
/// before anyone is asked.</para>
///
/// <para><b>Each acceptance is recorded the moment it is given</b>, with its
/// own timestamp, so the audit trail carries the real sequence of consent
/// rather than one batch time. So a decline part-way through leaves the
/// acceptances before it recorded: those members did agree.</para>
///
/// <para><b>A failure to record does not stop the round.</b> The member
/// consented in front of the operator, so that is honoured and the next
/// member is asked. ComplianceService swallows its own database errors, so
/// a throw reaching here would be unusual.</para>
/// </summary>
public sealed class ConsentRound
{
	private static readonly ILogger Logger = AppLogger.ForContext<ConsentRound>();

	private readonly IPrivacyPolicyCache _policyCache;
	private readonly IComplianceRegistration _compliance;

	public ConsentRound(IPrivacyPolicyCache policyCache, IComplianceRegistration compliance)
	{
		_policyCache = policyCache ?? throw new ArgumentNullException(nameof(policyCache));
		_compliance = compliance ?? throw new ArgumentNullException(nameof(compliance));
	}

	/// <summary>
	/// Runs the round over <paramref name="members"/>, in order.
	/// </summary>
	/// <param name="members">Who to ask; see <see cref="RegistrationGate.NeedingConsent"/>.</param>
	/// <param name="ask">
	/// Shows the policy and returns whether it was accepted. Given a title
	/// naming the member — <c>"{policy title} — {name}"</c> — and the
	/// policy body. A dismissal without a choice should return false.
	/// </param>
	/// <param name="fallbackName">
	/// Who the title names when a member has no anonymous name, e.g.
	/// "this GSR".
	/// </param>
	/// <param name="ct">Passed to the recording of each acceptance.</param>
	public async Task<ConsentOutcome> RunAsync(
		IEnumerable<Member> members,
		Func<string, string, Task<bool>> ask,
		string fallbackName,
		CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(members);
		ArgumentNullException.ThrowIfNull(ask);

		var policy = _policyCache.GetCached();
		if (policy is null)
		{
			Logger.Error(
				"No cached privacy policy on device; refusing to prompt for consent. " +
				"This indicates the sync-stage gate was bypassed.");
			return ConsentOutcome.NoPolicy;
		}

		if (string.IsNullOrWhiteSpace(policy.Policy))
		{
			Logger.Error(
				"Cached privacy policy {PolicyId} v{Version} has empty body; refusing to prompt for consent",
				policy.Id, policy.Version);
			return ConsentOutcome.EmptyPolicy;
		}

		foreach (var member in members)
		{
			// Both the title and the body come from the cached Scrutiny
			// record — Scrutiny is the single source of truth for what the
			// member sees, what the audit trail records, and what the
			// confirmation email quotes.
			var name = !string.IsNullOrWhiteSpace(member.AnonymousName) ? member.AnonymousName : fallbackName;

			if (!await ask($"{policy.Title} — {name}", policy.Policy))
			{
				Logger.Information(
					"GDPR consent declined for member {MemberId} ({Name})",
					member.Id, member.AnonymousName);
				return ConsentOutcome.Declined;
			}

			// The statement parameter is no longer used by ComplianceService,
			// which sources the wording from the cache itself; it is passed
			// the body the member just saw for continuity, in case a future
			// change starts honouring it again.
			var acceptedAt = DateTime.UtcNow;
			try
			{
				await _compliance.RecordAcceptance(
					member,
					version: policy.Version,
					statement: policy.Policy,
					method: "register-app",
					acceptedAtUtc: acceptedAt,
					ct: ct);

				// Mirror the in-memory entity so a page bound to it reflects
				// the new state at once — ComplianceService updates a freshly
				// loaded Member, not the one the caller holds.
				member.GdprAccepted = true;
				member.GdprAcceptedAt = acceptedAt;
			}
			catch (Exception ex)
			{
				Logger.Warning(ex,
					"Failed to record GDPR acceptance for member {MemberId} ({Name})",
					member.Id, member.AnonymousName);
			}
		}

		return ConsentOutcome.Consented;
	}
}
