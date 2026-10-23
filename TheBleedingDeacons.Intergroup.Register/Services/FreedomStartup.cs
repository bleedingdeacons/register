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
	/// <para><b>Freedom is where every credential and endpoint comes from</b>
	/// — see <see cref="FreedomSettings"/>. Without it the tablet has no SMTP,
	/// Unity or Better Stack settings at all.</para>
	///
	/// <para><b>Android only.</b> Freedom.Client.Maui's sign-in and device
	/// identity are Android's (WebAuthenticator, ANDROID_ID). Off Android
	/// nothing is registered, <see cref="ConfigurationService"/> gets no
	/// client, and the Windows head runs unconfigured.</para>
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

				// Which parts of the running tablet hold what changed is
				// FreedomChangePlan's to say, key by key; this only acts on it.
				var plan = FreedomChangePlan.For(change.Updated, change.Removed);

				if (plan.Has(FreedomEffects.Smtp))
				{
					services.GetRequiredService<IEmailService>()
						.UpdateConfigurationAsync(config.GetSmtpConfiguration())
						.SafeFireAndForget("FreedomSmtpUpdate");
				}

				if (plan.Has(FreedomEffects.LogShipping))
				{
					services.GetRequiredService<ILogShipper>()
						.Ship(config.GetBetterStackConfiguration());
				}

				if (plan.Has(FreedomEffects.UnitySite))
				{
					WarnIfUnitySiteChangedAsync(services).SafeFireAndForget("FreedomUnitySiteCheck");
				}

				if (plan.Unused.Count > 0)
				{
					Logger.Information(
						"Freedom holds {Keys}, which this build does not read; the site may be set up for a newer build",
						string.Join(", ", plan.Unused));
				}

				Logger.Information(
					"Freedom changed {Keys}; applied {Effects}",
					string.Join(", ", change.Updated.Concat(change.Removed)), plan.Effects);
			}
			catch (Exception ex)
			{
				// The new values are stored and will be used on the next start
				// whatever happens here.
				Logger.Warning(ex, "Freedom: a configuration change could not be applied until the next start");
			}
		}

		/// <summary>
		/// Tells the operator, as soon as it happens, that the tablet now
		/// points at a different Unity site from the one its records came
		/// from — rather than leaving them to find out at Finish Meeting.
		/// Nothing is changed here: <see cref="UnitySiteGuard"/> refuses the
		/// load or finish that would mix the two, whenever it is tried.
		/// </summary>
		private static async Task WarnIfUnitySiteChangedAsync(IServiceProvider services)
		{
			var check = await services.GetRequiredService<UnitySiteGuard>().CheckAsync().ConfigureAwait(false);
			if (!check.Changed)
				return;

			Logger.Warning(
				"Freedom moved Unity from {RecordsFrom} to {ConfiguredSite}; unsent work: {HasUnsentWork}",
				check.RecordsFrom, check.ConfiguredSite, check.HasUnsentWork);

			var message = check.HasUnsentWork
				? $"This tablet's records came from {check.RecordsFrom}, and have not all been sent. " +
				  $"Freedom now points it at {check.ConfiguredSite}, so they will not be sent there. " +
				  $"Point Freedom back at {check.RecordsFrom} and finish the meeting, or Reset Device to discard them."
				: $"This tablet's records came from {check.RecordsFrom}. Freedom now points it at " +
				  $"{check.ConfiguredSite}: Load Unity before the next meeting.";

			using var scope = services.CreateScope();
			var popups = scope.ServiceProvider.GetRequiredService<IPopupNotification>();
			await MainThread.InvokeOnMainThreadAsync(() => popups.ShowErrorAsync("Unity site changed", message)).ConfigureAwait(false);
		}
	}
}
