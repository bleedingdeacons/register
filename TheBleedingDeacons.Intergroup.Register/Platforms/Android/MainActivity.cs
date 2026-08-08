using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;

namespace TheBleedingDeacons.Intergroup.Register.Platforms.Android;

[Activity(Theme = "@style/Maui.SplashTheme",
	MainLauncher = true,
	ScreenOrientation = ScreenOrientation.Landscape,
	ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);
		HideNavigationBar();
	}

	public override void OnWindowFocusChanged(bool hasFocus)
	{
		base.OnWindowFocusChanged(hasFocus);
		if (hasFocus) HideNavigationBar();
	}

	private void HideNavigationBar()
	{
		// Held in a local so the null test narrows for both arguments below —
		// testing Window.DecorView directly leaves Window itself nullable.
		var window = Window;
		if (window?.DecorView is not { } decorView) return;

		var controller = WindowCompat.GetInsetsController(window, decorView);
		if (controller is null) return;

		controller.Hide(WindowInsetsCompat.Type.NavigationBars());

		// Use the AndroidX compat constant rather than the platform
		// WindowInsetsControllerBehavior enum: the latter is API 30+, and this
		// app supports API 21, so referencing it directly trips CA1416 and would
		// throw on pre-30 devices. WindowInsetsControllerCompat has the same
		// value and handles the older code path internally.
		controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
	}
}