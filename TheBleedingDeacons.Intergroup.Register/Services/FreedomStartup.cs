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
	/// <para><b>Nothing waits on it.</b> The start reads what Freedom stored
	/// last time — a secure-storage read, no request — in the background, so
	/// a slow keystore cannot hold up the first screen. Once it is read, what
	/// it holds is applied: the configuration cache is cleared, the email
	/// service is given the SMTP settings, and the log shipper is told where
	/// to ship. The sync follows, and anything it changes is applied the same
	/// way. A tablet in a hall with no signal starts on what it had.</para>
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
		/// Read the stored configuration and then sync, both in the background.
		/// Call once the container is built.
		/// </summary>
		/// <returns>
		/// Whether Freedom is on. If it is, Freedom tells the log shipper where
		/// to ship once the stored settings are read, and the caller must not:
		/// told before then, the shipper would act on the tablet's settings
		/// without Freedom's, and with none it drops what it has been holding.
		/// </returns>
		public static bool Start(IServiceProvider services)
		{
			var freedom = services.GetService<FreedomClient>();
			if (freedom is null)
				return false;

			freedom.ConfigChanged += (_, e) => ApplyChanges(services, e);

			StartAsync(services, freedom).SafeFireAndForget("FreedomStartup");
			return true;
		}

		private static async Task StartAsync(IServiceProvider services, FreedomClient freedom)
		{
			try
			{
				// Local only: SecureStorage, no request.
				await freedom.LoadAsync().ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Freedom: the stored configuration could not be read; starting without it");
			}

			ApplyLoaded(services);

			await SyncAsync(freedom).ConfigureAwait(false);
		}

		/// <summary>
		/// What was read from the store, applied to the parts of the running
		/// tablet that may have read settings before it arrived.
		/// </summary>
		private static void ApplyLoaded(IServiceProvider services)
		{
			try
			{
				var config = services.GetRequiredService<IConfigurationService>();
				config.InvalidateCache();

				services.GetRequiredService<ILogShipper>().Ship(config.GetBetterStackConfiguration());

				services.GetRequiredService<IEmailService>()
					.UpdateConfigurationAsync(config.GetSmtpConfiguration())
					.SafeFireAndForget("FreedomSmtpLoad");
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Freedom: the stored configuration could not be applied");
			}
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
