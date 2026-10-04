using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;

namespace TheBleedingDeacons.Intergroup.Register.Services;

/// <summary>
/// The rules that decide whether a group or a position may be registered,
/// and whose consent has to be captured first.
///
/// <para>These used to be written out in the two Verify view models — the
/// contact check four times over, the consent filter three — and the copies
/// had to be kept in step by hand. They live here so there is one of each,
/// and so they can be specified from a test host, which cannot reach a
/// view model. What stayed behind is the MAUI half: the buttons, the
/// popups, the navigation.</para>
///
/// <para>Everything here is deterministic: no state, and nothing read but
/// what is passed in.</para>
/// </summary>
public static class RegistrationGate
{
	/// <summary>
	/// True when at least one of <paramref name="holders"/> can be
	/// contacted: an anonymous name, and a mobile number or a personal
	/// email.
	///
	/// <para><b>In practice it is the email that counts.</b> Unity requires
	/// a personal email on every member and so does the tablet's own member
	/// form, so a holder with only a phone does not occur; the rule accepts
	/// one because it was written that way, not because the case is met.
	/// What does happen is a group with no GSR at all, and then there is
	/// nobody to reach and this is false.</para>
	///
	/// <para>Empty, not whitespace, is what counts as missing. A name of a
	/// single space passes. That is how the view models behaved, and moving
	/// the rule was not the moment to change it.</para>
	/// </summary>
	public static bool HasContactableHolder(IEnumerable<Member> holders)
	{
		ArgumentNullException.ThrowIfNull(holders);

		return holders.Any(h =>
			!string.IsNullOrEmpty(h.AnonymousName) &&
			(!string.IsNullOrEmpty(h.MobileNumber) || !string.IsNullOrEmpty(h.PersonalEmail)));
	}

	/// <summary>
	/// True unless someone is standing in for the GSR without saying who.
	/// When <paramref name="standingIn"/> is false the name is not required.
	/// </summary>
	public static bool StandInSatisfied(bool standingIn, string? standinName) =>
		!standingIn || !string.IsNullOrWhiteSpace(standinName);

	/// <summary>Whether a group may be registered: a contactable GSR, and a named stand-in if there is one.</summary>
	public static bool CanRegisterGroup(IEnumerable<Member> gsrs, bool standingIn, string? standinName) =>
		HasContactableHolder(gsrs) && StandInSatisfied(standingIn, standinName);

	/// <summary>Whether a position may be registered: a contactable holder.</summary>
	public static bool CanRegisterPosition(IEnumerable<Member> holders) =>
		HasContactableHolder(holders);

	/// <summary>
	/// The members who must accept the privacy policy before they are
	/// registered: anyone who has never accepted it, and anyone whose
	/// recorded acceptance is for a different version from
	/// <paramref name="cachedPolicyVersion"/>.
	///
	/// <para><b>Different, not older.</b> Versions are free-form text in
	/// Scrutiny's contract, so there is no ordering to compare by; any
	/// version other than the current one is out of date. The comparison
	/// is ordinal, so "2.1" and "v2.1" differ, and so do two cases of the
	/// same letters.</para>
	///
	/// <para>With no cached version only the never-accepted test applies.
	/// <see cref="ConsentRound"/> then refuses to ask anyone, which is what
	/// surfaces the missing policy to the operator.</para>
	/// </summary>
	public static IReadOnlyList<Member> NeedingConsent(IEnumerable<Member> members, string? cachedPolicyVersion)
	{
		ArgumentNullException.ThrowIfNull(members);

		return members.Where(m =>
				m.GdprAccepted != true
				|| (!string.IsNullOrWhiteSpace(cachedPolicyVersion)
					&& !string.Equals(m.GdprAcceptanceVersion, cachedPolicyVersion, StringComparison.Ordinal)))
			.ToList();
	}

	/// <summary>
	/// The holders of the positions that registering <paramref name="gsrs"/>
	/// will cascade to, who must accept the policy first.
	///
	/// <para>When positions are auto-registered with a group, a GSR who
	/// holds an intergroup position takes that position with them — and its
	/// other holders, who may sit in other groups, never appear among the
	/// GSRs and so never meet the first consent round. This finds them, so
	/// their consent is captured in the same interaction that registers
	/// them.</para>
	///
	/// <para>Holders are loaded fresh from <paramref name="positions"/>, so
	/// a GSR who accepted in the first round has that acceptance on the row
	/// and drops out here. The GSR objects the view model holds are not
	/// updated with the version, which is why this reads the repository
	/// rather than them.</para>
	/// </summary>
	public static async Task<IReadOnlyList<Member>> CascadedHoldersNeedingConsentAsync(
		IEnumerable<Member> gsrs,
		IPositionRepository positions,
		string? cachedPolicyVersion,
		CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(gsrs);
		ArgumentNullException.ThrowIfNull(positions);

		var positionIds = gsrs
			.Where(m => m.IntergroupPositionId.HasValue)
			.Select(m => m.IntergroupPositionId!.Value)
			.Distinct()
			.ToList();

		var needing = new List<Member>();
		foreach (var positionId in positionIds)
		{
			var position = await positions.GetByIdWithHoldersAsync(positionId, ct).ConfigureAwait(false);
			if (position?.Holders == null)
			{
				continue;
			}

			needing.AddRange(NeedingConsent(position.Holders, cachedPolicyVersion));
		}

		return needing;
	}
}
