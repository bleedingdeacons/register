using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Register.Services;

/// <summary>
/// The heads with no screen-lock prompt: Windows, iOS and Mac. The tablet
/// that goes round meetings is Android, and the Windows head has no Freedom
/// sign-in and so holds no settings or member data worth guarding.
/// </summary>
public sealed class NoScreenLockPrompt : IScreenLockPrompt
{
	public Task<ScreenLockResult> ConfirmAsync(string title, string description) =>
		Task.FromResult(ScreenLockResult.NotOnThisPlatform);
}
