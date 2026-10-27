using Android.App;
using Android.Content;
using Android.Hardware.Biometrics;
using Android.OS;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Register.Services;

/// <summary>
/// Android's own screen-lock prompt: the tablet's PIN, pattern or password,
/// and nothing else — a fingerprint enrolled on a shared tablet could be
/// anybody's.
///
/// <para><b>Two prompts, by Android version.</b> From Android 11 (API 30) a
/// <see cref="BiometricPrompt"/> can be told to accept the device credential
/// alone. Below that it cannot, so the older
/// <see cref="KeyguardManager.CreateConfirmDeviceCredentialIntent(string, string)"/>
/// is used, whose answer comes back through
/// <see cref="Platforms.Android.MainActivity"/>'s <c>OnActivityResult</c>.
/// The app supports API 24, so both are needed.</para>
///
/// <para><b>No screen lock, no way in.</b> A tablet with no lock set has no
/// code to ask for, and letting it through would make the gate decorative.
/// The operator is told to set one.</para>
/// </summary>
public sealed class AndroidScreenLockPrompt : IScreenLockPrompt
{
	// Any value that fits the 16 bits Android allows a request code.
	private const int ConfirmCredentialRequest = 0x5C1;

	private static TaskCompletionSource<bool>? _pendingConfirm;

	public Task<ScreenLockResult> ConfirmAsync(string title, string description) =>
		MainThread.InvokeOnMainThreadAsync(() => AskAsync(title, description));

	/// <summary>
	/// Hands the older prompt's answer back. Called from
	/// <c>MainActivity.OnActivityResult</c>; true when the result was ours.
	/// </summary>
	internal static bool OnActivityResult(int requestCode, Result resultCode)
	{
		if (requestCode != ConfirmCredentialRequest)
			return false;

		Interlocked.Exchange(ref _pendingConfirm, null)?.TrySetResult(resultCode == Result.Ok);
		return true;
	}

	private static async Task<ScreenLockResult> AskAsync(string title, string description)
	{
		var activity = Platform.CurrentActivity;
		if (activity is null)
			return ScreenLockResult.Declined;

		if (activity.GetSystemService(Context.KeyguardService) is not KeyguardManager keyguard || !keyguard.IsDeviceSecure)
			return ScreenLockResult.NoScreenLock;

		if (OperatingSystem.IsAndroidVersionAtLeast(30))
			return await AskWithBiometricPromptAsync(activity, title, description);

		return await AskWithKeyguardAsync(activity, keyguard, title, description);
	}

	[System.Runtime.Versioning.SupportedOSPlatform("android30.0")]
	private static async Task<ScreenLockResult> AskWithBiometricPromptAsync(Activity activity, string title, string description)
	{
		var done = new TaskCompletionSource<ScreenLockResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var cancel = new CancellationSignal();
		using var callback = new Callback(done);
		using var prompt = new BiometricPrompt.Builder(activity)
			.SetTitle(title)
			.SetDescription(description)
			.SetAllowedAuthenticators((int)BiometricManagerAuthenticators.DeviceCredential)
			.Build();

		prompt.Authenticate(cancel, activity.MainExecutor!, callback);

		return await done.Task;
	}

	private static async Task<ScreenLockResult> AskWithKeyguardAsync(Activity activity, KeyguardManager keyguard, string title, string description)
	{
		// Obsolete from API 29, and reached only below 30: its replacement
		// above needs 30, so on 29 this is still the only credential-only
		// prompt there is.
#pragma warning disable CA1422
		using var intent = keyguard.CreateConfirmDeviceCredentialIntent(title, description);
#pragma warning restore CA1422
		if (intent is null)
			return ScreenLockResult.NoScreenLock;

		var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		// A prompt left open behind this one can never be answered now.
		Interlocked.Exchange(ref _pendingConfirm, done)?.TrySetResult(false);

		activity.StartActivityForResult(intent, ConfirmCredentialRequest);

		return await done.Task ? ScreenLockResult.Confirmed : ScreenLockResult.Declined;
	}

	[System.Runtime.Versioning.SupportedOSPlatform("android28.0")]
	private sealed class Callback(TaskCompletionSource<ScreenLockResult> done) : BiometricPrompt.AuthenticationCallback
	{
		public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? result) =>
			done.TrySetResult(ScreenLockResult.Confirmed);

		// Terminal: cancelled, timed out, locked out, or no credential set.
		// A wrong code is not reported here; the prompt lets them try again.
		public override void OnAuthenticationError(BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString) =>
			done.TrySetResult(errorCode == BiometricErrorCode.NoDeviceCredential
				? ScreenLockResult.NoScreenLock
				: ScreenLockResult.Declined);
	}
}
