using Android.App;
using Android.Content.PM;
using Android.OS;

namespace TheBleedingDeacons.Intergroup.Register.Platforms.Android;

[Activity(Theme = "@style/Maui.SplashTheme", 
	MainLauncher = true,
	ScreenOrientation = ScreenOrientation.Landscape,
	ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
}