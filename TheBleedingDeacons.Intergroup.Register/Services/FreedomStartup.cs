using Serilog;
using TheBleedingDeacons.Freedom.Client;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Inventory;
#if ANDROID
using TheBleedingDeacons.Freedom.Client.Maui;
#endif

namespace TheBleedingDeacons.Intergroup.Register.Services
{
	/// <summary>
	/// Wires Freedom into Register: registration at build time, and on every
	/// start a local read followed by a background sync.
	///
	/// <para><b>Android only, for now.</b> Freedom.Client.Maui's sign-in and
	/// device identity are Android's (WebAuthenticator, ANDROID_ID), and what a
	/// Windows or iOS tablet should be identified by is still open. Off
	/// Android nothing is registered, <see cref="ConfigurationService"/> gets
	/// no client, and the app behaves exactly as it did.</para>
	///
	/// <para><b>Nothing waits on the network.</b> The start reads what Freedom
	/// stored last time — a secure-storage read, no request — so the settings
	/// the rest of startup builds from are already Freedom's. The sync then
	/// runs in the background, and anything it changes is applied at once:
	/// the configuration cache is cleared, the email service is given the new
	/// SMTP settings, and the Better Stack sink is rebuilt. A tablet in a hall
	/// with no signal starts on what it had.</para>
	/// </summary>
	public static class FreedomStartup
	{
		private static readonly ILogger Logger = AppLogger.ForContext(nameof(FreedomStartup));

		/// <summary>Register Freedom when the build names a site. Returns whether it did.</summary>
		public static bool Register(MauiAppBuilder builder)
		{
#if ANDROID
			var options = FreedomSettings.OptionsFrom(builder.Configuration);
			if (options is null)
			{
				Logger.Information("Freedom is off: this build names no Freedom site");
				return false;
			}

			builder.UseFreedom(options);
			Logger.Information("Freedom is on: {Site}, application {Application}", options.BaseUrl, options.Application);
			return true;
#else
			return false;
#endif
		}

		/// <summary>
		/// Read the stored configuration now, and sync in the background.
		/// Call once the container is built and before anything reads settings.
		/// </summary>
		public static void Start(IServiceProvider services)
		{
			var freedom = services.GetService<FreedomClient>();
			if (freedom is null)
				return;

			try
			{
				// Local only: SecureStorage, no request. Bounded all the same,
				// so a keystore that hangs cannot hold up the first screen.
				Task.Run(() => freedom.LoadAsync()).Wait(TimeSpan.FromSeconds(3));
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Freedom: the stored configuration could not be read; starting without it");
			}

			freedom.ConfigChanged += (_, e) => ApplyChanges(services, e);

			SyncAsync(freedom).SafeFireAndForget("FreedomStartupSync");
		}

		public static async Task<SyncResult> SyncAsync(FreedomClient freedom)
		{
			var result = await freedom.SyncAsync().ConfigureAwait(false);

			// Keys only, never values — the same rule the library keeps.
			Logger.Information(
				"Freedom sync {Status}: {Updated} updated, {Removed} removed, verified {VerifiedAt} — {Message}",
				result.Status, result.Updated.Count, result.Removed.Count, result.VerifiedAt, result.Message);

			return result;
		}

		private static void ApplyChanges(IServiceProvider services, ConfigChangedEventArgs change)
		{
			try
			{
				var config = services.GetRequiredService<IConfigurationService>();
				config.InvalidateCache();

				var touched = change.Updated.Concat(change.Removed).ToList();

				if (touched.Any(k => k.StartsWith("smtp.", StringComparison.Ordinal)))
				{
					services.GetRequiredService<IEmailService>()
						.UpdateConfigurationAsync(config.GetSmtpConfiguration())
						.SafeFireAndForget("FreedomSmtpUpdate");
				}

				if (touched.Any(k => k.StartsWith("betterstack.", StringComparison.Ordinal)))
				{
					services.GetRequiredService<ILogShipper>()
						.Ship(config.GetBetterStackConfiguration());
				}

				Logger.Information("Freedom changed {Keys}; applied", string.Join(", ", touched));
			}
			catch (Exception ex)
			{
				// The new values are stored and will be used on the next start
				// whatever happens here.
				Logger.Warning(ex, "Freedom: a configuration change could not be applied until the next start");
			}
		}
	}
}
