using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Intergroup.Register.Services;

/// <summary>
/// Runs a <see cref="ConsentRound"/> through the app's popups: the policy
/// in the terms popup, and an error when the round refuses to start.
///
/// <para>The round itself is in Register.Core; this is the half that is
/// MAUI. Shared by both Verify view models, so the refusal wording is
/// written once.</para>
/// </summary>
public static class ConsentPrompt
{
	/// <summary>
	/// Asks <paramref name="members"/> to accept the cached policy. True only
	/// when every one of them did.
	/// </summary>
	public static async Task<bool> AskAsync(
		this ConsentRound round,
		IEnumerable<Member> members,
		IPopupNotification popups,
		string fallbackName)
	{
		ArgumentNullException.ThrowIfNull(round);
		ArgumentNullException.ThrowIfNull(popups);

		switch (await round.RunAsync(members, popups.ShowTerms, fallbackName))
		{
			case ConsentOutcome.Consented:
				return true;

			case ConsentOutcome.NoPolicy:
				await popups.ShowErrorAsync(
					"Cannot record consent",
					"This device has no active privacy policy on record. " +
					"Re-sync from the Admin page before continuing.");
				return false;

			case ConsentOutcome.EmptyPolicy:
				await popups.ShowErrorAsync(
					"Cannot record consent",
					"The cached privacy policy has no body text on record. " +
					"Re-sync from the Admin page before continuing.");
				return false;

			default:
				return false;
		}
	}
}
