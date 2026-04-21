namespace TheBleedingDeacons.Intergroup.Register.Controls;

/// <summary>
/// Platform-specific helpers for dismissing the soft keyboard / IME.
///
/// Calling Entry.Unfocus() on its own is enough on iOS / Mac / Windows, but on Android
/// the InputMethodManager keeps the IME visible until it is explicitly told to hide it.
/// This partial centralises the workaround so callers don't have to care about platform.
/// </summary>
internal static partial class KeyboardHelper
{
	/// <summary>
	/// Request the platform dismiss the soft keyboard attached to the given view.
	/// </summary>
	/// <param name="view">
	/// The control currently holding keyboard focus (e.g. the internal Entry of a
	/// CloseableEntry). Used on Android to resolve the platform IBinder token.
	/// </param>
	public static partial void HideKeyboard(Microsoft.Maui.Controls.View view);
}