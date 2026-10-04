using Serilog;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Services;

/// <summary>
/// Removes, once, the settings earlier builds kept on the tablet.
///
/// <para>Before credentials came only from Freedom, a tablet could hold an
/// SMTP password, a Unity API key and a Better Stack token in SecureStorage,
/// the rest of those connections in three JSON files, and a compliance
/// address in Preferences. Nothing reads any of it now, and a password
/// nothing reads is still a password on a device that gets passed around a
/// meeting room, so it goes.</para>
///
/// <para>Runs in the background, once per install — a flag in Preferences
/// records that it has — and never throws: a key that will not delete is
/// logged and left for the next start.</para>
/// </summary>
public static class LegacySettings
{
	private const string DoneKey = "legacy_settings_forgotten";

	private static readonly string[] SecureKeys = ["smtp_password", "unity_api_key", "betterstack_source_token"];

	private static readonly string[] PreferenceKeys = ["compliance_email"];

	private static readonly string[] Files = ["mailsettings.json", "unitysettings.json", "betterstacksettings.json"];

	private static readonly ILogger Logger = AppLogger.ForContext(nameof(LegacySettings));

	public static void Forget()
	{
		try
		{
			if (Preferences.Get(DoneKey, false))
			{
				return;
			}
		}
		catch (Exception ex)
		{
			Logger.Warning(ex, "Could not read whether earlier settings were removed; leaving them for now");
			return;
		}

		Task.Run(ForgetNow).SafeFireAndForget("ForgetLegacySettings");
	}

	private static void ForgetNow()
	{
		var complete = true;

		foreach (var key in SecureKeys)
		{
			complete &= Try($"secure key {key}", () => SecureStorage.Remove(key));
		}

		foreach (var key in PreferenceKeys)
		{
			complete &= Try($"preference {key}", () => Preferences.Remove(key));
		}

		foreach (var file in Files)
		{
			var path = Path.Combine(FileSystem.AppDataDirectory, file);
			complete &= Try($"file {file}", () =>
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			});
		}

		if (complete)
		{
			Preferences.Set(DoneKey, true);
			Logger.Information("Removed the settings earlier builds kept on this tablet");
		}
	}

	private static bool Try(string what, Action remove)
	{
		try
		{
			remove();
			return true;
		}
		catch (Exception ex)
		{
			Logger.Warning(ex, "Could not remove the earlier {What}; will try again next start", what);
			return false;
		}
	}
}
