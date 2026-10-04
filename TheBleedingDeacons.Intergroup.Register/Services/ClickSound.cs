using Microsoft.Maui.Handlers;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Services;

/// <summary>
/// Marks a page or popup as part of the registration workflow: every button
/// on it plays <see cref="ClickSound"/> when tapped. Pages outside the
/// workflow — Settings, Admin, the diagnostics — stay silent.
/// </summary>
public interface IRegistrationWorkflow;

/// <summary>
/// Audible confirmation that a tap on the registration workflow registered.
///
/// <para><b>Why not Android's own click.</b> Android buttons already play the
/// system key-click — but only when Touch sounds is on, through the system
/// stream. The tablets have Touch sounds off and the system stream muted, so
/// that click is silent. This plays a bundled 30 ms sample
/// (<c>Platforms/Android/Resources/raw/click.wav</c>, generated rather than
/// downloaded) through <see cref="Android.Media.SoundPool"/> on the
/// <b>media</b> stream, which is the one left audible. When Touch sounds is
/// on, Android has already clicked, so this stays quiet rather than clicking
/// twice.</para>
///
/// <para><b>How it is wired.</b> One mapping on every Button's handler
/// subscribes to Clicked; at the tap, the button's ancestors are walked for
/// an <see cref="IRegistrationWorkflow"/> page or popup. A button can opt out
/// with <c>services:ClickSound.Off="True"</c>. The tablet's Settings switch
/// (<see cref="IConfigurationService.IsButtonSoundEnabled"/>) is read at each
/// tap, so flipping it takes effect at once.</para>
/// </summary>
public static class ClickSound
{
	private static readonly ILogger Logger = AppLogger.ForContext(nameof(ClickSound));

	/// <summary>True on a button that should stay silent on a workflow page.</summary>
	public static readonly BindableProperty OffProperty =
		BindableProperty.CreateAttached("Off", typeof(bool), typeof(ClickSound), false);

	// Set once a button's Clicked is subscribed, so a handler reconnecting
	// (a singleton page pushed again) cannot subscribe it twice.
	private static readonly BindableProperty HookedProperty =
		BindableProperty.CreateAttached("Hooked", typeof(bool), typeof(ClickSound), false);

	public static bool GetOff(BindableObject view) => (bool)view.GetValue(OffProperty);

	public static void SetOff(BindableObject view, bool value) => view.SetValue(OffProperty, value);

	/// <summary>Hook every Button, and start loading the sample. Call once at startup.</summary>
	public static void Register()
	{
		ButtonHandler.Mapper.AppendToMapping(nameof(ClickSound), (_, view) =>
		{
			if (view is Button button && !(bool)button.GetValue(HookedProperty))
			{
				button.SetValue(HookedProperty, true);
				button.Clicked += OnClicked;
			}
		});

		Preload();
	}

	private static void OnClicked(object? sender, EventArgs e)
	{
		if (sender is not Button button || GetOff(button) || !InWorkflow(button))
			return;

		var config = IPlatformApplication.Current?.Services.GetService<IConfigurationService>();
		if (config?.IsButtonSoundEnabled != true)
			return;

		Play();
	}

	private static bool InWorkflow(Element element)
	{
		for (Element? current = element; current is not null; current = current.Parent)
		{
			if (current is IRegistrationWorkflow)
				return true;
		}

		return false;
	}

#if ANDROID
	private static Android.Media.SoundPool? _pool;
	private static int _soundId;
	private static volatile bool _loaded;

	private static void Preload()
	{
		try
		{
			var attributes = new Android.Media.AudioAttributes.Builder()
				.SetUsage(Android.Media.AudioUsageKind.Media)!
				.SetContentType(Android.Media.AudioContentType.Sonification)!
				.Build();

			_pool = new Android.Media.SoundPool.Builder()
				.SetMaxStreams(2)!
				.SetAudioAttributes(attributes)!
				.Build()!;

			_pool.LoadComplete += (_, args) => _loaded = args.Status == 0;
			_soundId = _pool.Load(Android.App.Application.Context, Resource.Raw.click, 1);
		}
		catch (Exception ex)
		{
			// No click is a cosmetic loss; it must never stop the app starting.
			Logger.Warning(ex, "Could not load the button click sound; taps will be silent");
		}
	}

	private static void Play()
	{
		try
		{
			// Android has already played its own click if Touch sounds is on.
			var touchSounds = Android.Provider.Settings.System.GetInt(
				Android.App.Application.Context.ContentResolver, Android.Provider.Settings.System.SoundEffectsEnabled, 0);
			if (touchSounds == 1)
				return;

			if (_pool is not null && _loaded)
				_pool.Play(_soundId, 1f, 1f, 1, 0, 1f);
		}
		catch (Exception ex)
		{
			Logger.Debug(ex, "Button click sound could not play");
		}
	}
#else
	private static void Preload()
	{
	}

	private static void Play()
	{
	}
#endif
}
