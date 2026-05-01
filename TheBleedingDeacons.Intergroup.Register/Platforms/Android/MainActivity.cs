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
		if (Window is null) return;

		var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);
		controller.Hide(WindowInsetsCompat.Type.NavigationBars());
		controller.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
	}
}