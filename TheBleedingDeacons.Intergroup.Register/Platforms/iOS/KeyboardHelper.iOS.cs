namespace TheBleedingDeacons.Intergroup.Register.Controls;

internal static partial class KeyboardHelper
{
	// iOS: resigning first responder (what Entry.Unfocus() does under the hood)
	// is enough; no additional action required.
	public static partial void HideKeyboard(Microsoft.Maui.Controls.View view) { }
}