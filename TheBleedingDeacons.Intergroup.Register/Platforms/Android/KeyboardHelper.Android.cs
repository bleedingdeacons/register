using Android.Content;
using Android.Views.InputMethods;
using Microsoft.Maui.Platform;

namespace TheBleedingDeacons.Intergroup.Register.Controls;

internal static partial class KeyboardHelper
{
	/// <summary>
	/// Android: Entry.Unfocus() clears logical focus but doesn't toggle the IME —
	/// we have to ask InputMethodManager ourselves. The InputMethodService is
	/// per-Activity, so we pull the context from the native view and grab the
	/// window token off of whichever view currently hosts the IME.
	/// </summary>
	public static partial void HideKeyboard(Microsoft.Maui.Controls.View view)
	{
		try
		{
			if (view?.Handler?.PlatformView is not Android.Views.View platformView)
				return;

			var imm = (InputMethodManager?)platformView.Context?.GetSystemService(Context.InputMethodService);
			if (imm is null)
				return;

			// Prefer the token of whatever is currently focused in the window; fall back
			// to the platform view we were given.
			var token = platformView.WindowToken;
			if (token is null)
				return;

			imm.HideSoftInputFromWindow(token, HideSoftInputFlags.None);
		}
		catch
		{
			// Swallow — dismissing the IME must never crash the app. If it can't be
			// hidden for whatever reason, the user can still tap Back.
		}
	}
}