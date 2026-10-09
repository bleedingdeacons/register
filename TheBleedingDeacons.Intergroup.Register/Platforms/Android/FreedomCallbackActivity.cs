using Android.App;
using Android.Content;
using Android.Content.PM;

namespace TheBleedingDeacons.Intergroup.Register;

/// <summary>
/// Catches the redirect that ends a Freedom sign-in's browser leg.
///
/// <para>The scheme and host here must agree with two other places:
/// <c>FreedomSettings.FreedomCallbackUri</c> (or the <c>Freedom:CallbackUri</c>
/// setting), and the <c>register</c> application's Callback URI in the
/// Freedom admin. A mismatch shows up as a browser tab that opens and never
/// comes back, with nothing in any log to say why.</para>
///
/// <para>What arrives is a one-time code, worthless without the PKCE verifier
/// the app kept, so another app claiming this scheme gains nothing.</para>
/// </summary>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
	[Intent.ActionView],
	Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
	DataScheme = "org.thebleedingdeacons.intergroup.register.freedom",
	DataHost = "auth")]
public sealed class FreedomCallbackActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
}
