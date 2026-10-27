namespace TheBleedingDeacons.Intergroup.Register.Services.Interfaces;

/// <summary>What came of asking for the tablet's screen code.</summary>
public enum ScreenLockResult
{
	/// <summary>The code was given.</summary>
	Confirmed,

	/// <summary>The prompt was cancelled, or could not be shown.</summary>
	Declined,

	/// <summary>The tablet has no screen lock, so there is no code to ask for.</summary>
	NoScreenLock,

	/// <summary>
	/// This platform has no screen-lock prompt here. Only the Windows,
	/// iOS and Mac heads say this; Android never does.
	/// </summary>
	NotOnThisPlatform,
}

/// <summary>
/// Asks for the tablet's own screen code — its PIN, pattern or password —
/// through the platform's prompt. The app never sees the code, only whether
/// it was right.
/// </summary>
public interface IScreenLockPrompt
{
	Task<ScreenLockResult> ConfirmAsync(string title, string description);
}
