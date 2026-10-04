using Serilog;
using TheBleedingDeacons.Freedom.Client;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Inventory;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
	/// <summary>
	/// The tablet's settings, on a real device.
	///
	/// <para><b>Credentials and endpoints come from Freedom and nowhere
	/// else</b> — see <see cref="FreedomSettings"/>. There is no embedded
	/// devsettings.json, no settings file and no SecureStorage fallback: a
	/// tablet that is not signed in to Freedom, or a build that names no
	/// Freedom site, has no SMTP, Unity or Better Stack settings at all, and
	/// the pages that need them say so.</para>
	///
	/// <para>What stays here is the tablet's own: the feature switches, the
	/// device label and the active intergroup meeting, in Preferences.</para>
	/// </summary>
	public class ConfigurationService : IConfigurationService
	{
		private static readonly ILogger Logger = AppLogger.ForContext<ConfigurationService>();

		private const string COMPLIANCE_ACCEPTANCE_EMAIL_ENABLED_KEY = "compliance_acceptance_email_enabled";
		private const string UNITY_ACTIVE_MEETING_KEY = "unity_active_meeting_id";
		private const string REGISTRATION_LOG_ENABLED_KEY = "registration_log_enabled";
		private const string AUTO_REGISTER_POSITIONS_KEY = "auto_register_positions_on_group";
		private const string SINGLE_GSR_SHORTCUT_KEY = "single_gsr_shortcut_enabled";
		private const string ADD_POSITION_HOLDER_ENABLED_KEY = "add_position_holder_enabled";
		private const string COMPLIANCE_LOG_ENABLED_KEY = "compliance_log_enabled";
		private const string WELCOME_EMAIL_ENABLED_KEY = "welcome_email_on_registration_enabled";
		private const string DEVICE_LABEL_KEY = "device_label";

		// Null when the build names no Freedom site, or off Android. Every
		// credential and endpoint is then empty. See FreedomSettings.
		private readonly FreedomClient? _freedom;

		private SmtpConfiguration? _cachedSmtpConfig;
		private UnityConfiguration? _cachedUnityConfig;
		private BetterStackConfiguration? _cachedBetterStackConfig;

		public ConfigurationService()
			: this(null)
		{
		}

		public ConfigurationService(FreedomClient? freedom)
		{
			_freedom = freedom;

			if (_freedom is null)
			{
				Logger.Warning("No Freedom client: this tablet has no SMTP, Unity or Better Stack settings");
			}

			LegacySettings.Forget();
		}

		/// <summary>The value Freedom holds for a key, or null when it holds none.</summary>
		private string? Managed(string key) => _freedom?.Get(key);

		// =================================================================
		// SMTP, Unity, Better Stack, compliance contact — Freedom only
		// =================================================================

		public SmtpConfiguration GetSmtpConfiguration() =>
			_cachedSmtpConfig ??= FreedomSettings.Smtp(Managed);

		public Task<SmtpConfiguration> LoadSmtpConfigurationAsync()
		{
			_cachedSmtpConfig = FreedomSettings.Smtp(Managed);
			return Task.FromResult(_cachedSmtpConfig);
		}

		public Task<UnityConfiguration> LoadUnityConfigurationAsync()
		{
			var (baseUrl, apiKey) = FreedomSettings.Unity(Managed);

			_cachedUnityConfig = new UnityConfiguration
			{
				BaseUrl = baseUrl,
				ApiKey = apiKey,
				ActiveIntergroupMeetingId = LoadActiveIntergroupMeeting(),
			};

			return Task.FromResult(_cachedUnityConfig);
		}

		public BetterStackConfiguration GetBetterStackConfiguration() =>
			_cachedBetterStackConfig ??= FreedomSettings.BetterStack(Managed);

		public Task<BetterStackConfiguration> LoadBetterStackConfigurationAsync()
		{
			_cachedBetterStackConfig = FreedomSettings.BetterStack(Managed);
			return Task.FromResult(_cachedBetterStackConfig);
		}

		public string ComplianceEmail => FreedomSettings.Compliance(Managed);

		public void InvalidateCache()
		{
			_cachedSmtpConfig = null;
			_cachedUnityConfig = null;
			_cachedBetterStackConfig = null;
			Logger.Information("Configuration cache cleared after Freedom changed");
		}

		// =================================================================
		// Active intergroup meeting — the tablet's own
		// =================================================================

		public Task SaveActiveIntergroupMeetingAsync(int? meetingId)
		{
			try
			{
				if (meetingId.HasValue && meetingId.Value > 0)
					Preferences.Set(UNITY_ACTIVE_MEETING_KEY, meetingId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
				else
					Preferences.Remove(UNITY_ACTIVE_MEETING_KEY);
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Failed to save active intergroup meeting ID to Preferences");
			}

			if (_cachedUnityConfig != null)
				_cachedUnityConfig.ActiveIntergroupMeetingId = meetingId;

			Logger.Information("Active intergroup meeting set to {MeetingId}", meetingId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none");
			return Task.CompletedTask;
		}

		private static int? LoadActiveIntergroupMeeting()
		{
			try
			{
				var raw = Preferences.Get(UNITY_ACTIVE_MEETING_KEY, string.Empty);
				if (int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedId) && parsedId > 0)
					return parsedId;
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Failed to load active intergroup meeting ID from Preferences");
			}

			return null;
		}

		// =================================================================
		// Registration Event Log Toggle
		// =================================================================

		/// <summary>
		/// Reads the toggle from Preferences. Defaults to <c>true</c> when
		/// the preference has never been written, which means fresh installs
		/// get the durability layer automatically. A user who explicitly
		/// disables it persists as "false"; there's no way to end up
		/// accidentally off due to a missing key.
		/// </summary>
		public bool IsRegistrationEventLogEnabled
		{
			get
			{
				try
				{
					// Preferences has no first-class bool accessor, so we
					// store the string "true"/"false". Missing key →
					// default true (safe / on by default).
					var raw = Preferences.Get(REGISTRATION_LOG_ENABLED_KEY, string.Empty);
					if (string.IsNullOrEmpty(raw)) return true;
					return !bool.TryParse(raw, out var value) || value;
				}
				catch (Exception ex)
				{
					// If Preferences is unavailable (extremely rare — only
					// on a broken install), fail safe by treating the log as on.
					Logger.Warning(ex, "Failed to read registration log toggle — defaulting to enabled");
					return true;
				}
			}
		}

		public void SetRegistrationEventLogEnabled(bool enabled)
		{
			try
			{
				Preferences.Set(REGISTRATION_LOG_ENABLED_KEY, enabled ? "true" : "false");
				Logger.Information("Registration event log {State}", enabled ? "ENABLED" : "DISABLED");
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Failed to save registration log toggle");
			}
		}

		// =================================================================
		// Auto-Register Positions on Group Registration Toggle
		// =================================================================

		/// <summary>
		/// Reads the toggle from Preferences. Defaults to <c>true</c> when the
		/// preference has never been written — the cascade saves an officer
		/// from tapping twice when they're also their group's GSR, which is
		/// the common case. Operators who want the old one-tap-per-entity
		/// behaviour can turn it off in Settings.
		/// </summary>
		public bool IsAutoRegisterPositionsOnGroupEnabled
		{
			get
			{
				try
				{
					var raw = Preferences.Get(AUTO_REGISTER_POSITIONS_KEY, string.Empty);
					if (string.IsNullOrEmpty(raw)) return true;
					return !bool.TryParse(raw, out var value) || value;
				}
				catch (Exception ex)
				{
					// If Preferences is unavailable, fail safe by treating the
					// toggle as on — matches the default for fresh installs
					// and keeps behaviour consistent across a broken-prefs edge case.
					Logger.Warning(ex, "Failed to read auto-register-positions toggle — defaulting to enabled");
					return true;
				}
			}
		}

		public void SetAutoRegisterPositionsOnGroupEnabled(bool enabled)
		{
			try
			{
				Preferences.Set(AUTO_REGISTER_POSITIONS_KEY, enabled ? "true" : "false");
				Logger.Information("Auto-register positions on group {State}", enabled ? "ENABLED" : "DISABLED");
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Failed to save auto-register-positions toggle");
			}
		}

		// =================================================================
		// Compliance Event Log Toggle
		// =================================================================

		/// <summary>
		/// Reads the toggle from Preferences. Defaults to <c>true</c> when
		/// the preference has never been written — same default-on policy
		/// as <see cref="IsRegistrationEventLogEnabled"/>, so fresh installs
		/// get the durability layer for both compliance and attendance
		/// without having to opt in.
		/// </summary>
		public bool IsComplianceEventLogEnabled
		{
			get
			{
				try
				{
					var raw = Preferences.Get(COMPLIANCE_LOG_ENABLED_KEY, string.Empty);
					if (string.IsNullOrEmpty(raw)) return true;
					return !bool.TryParse(raw, out var value) || value;
				}
				catch (Exception ex)
				{
					// Fail safe by treating the log as on — same logic
					// as the registration log toggle.
					Logger.Warning(ex, "Failed to read compliance log toggle — defaulting to enabled");
					return true;
				}
			}
		}

		public void SetComplianceEventLogEnabled(bool enabled)
		{
			try
			{
				Preferences.Set(COMPLIANCE_LOG_ENABLED_KEY, enabled ? "true" : "false");
				Logger.Information("Compliance event log {State}", enabled ? "ENABLED" : "DISABLED");
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Failed to save compliance log toggle");
			}
		}

		// =================================================================
		// Single-GSR Shortcut Toggle
		// =================================================================

		/// <summary>
		/// Reads the toggle from Preferences. Defaults to <c>false</c> when the
		/// preference has never been written — fresh installs get the explicit
		/// "always pick from the list, then tap Yes" flow. Operators who want
		/// the one-tap shortcut for single-GSR groups can turn it on in Settings.
		/// </summary>
		public bool IsSingleGsrShortcutEnabled
		{
			get
			{
				try
				{
					var raw = Preferences.Get(SINGLE_GSR_SHORTCUT_KEY, string.Empty);
					if (string.IsNullOrEmpty(raw)) return false;
					return bool.TryParse(raw, out var value) && value;
				}
				catch (Exception ex)
				{
					// If Preferences is unavailable, fail safe by treating the
					// shortcut as off — matches the default for fresh installs
					// and keeps behaviour consistent across a broken-prefs edge case.
					Logger.Warning(ex, "Failed to read single-GSR shortcut toggle — defaulting to disabled");
					return false;
				}
			}
		}

		public void SetSingleGsrShortcutEnabled(bool enabled)
		{
			try
			{
				Preferences.Set(SINGLE_GSR_SHORTCUT_KEY, enabled ? "true" : "false");
				Logger.Information("Single-GSR shortcut {State}", enabled ? "ENABLED" : "DISABLED");
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Failed to save single-GSR shortcut toggle");
			}
		}

		// =================================================================
		// Add-position-holder Toggle
		// =================================================================

		/// <summary>
		/// Reads the toggle from Preferences. Defaults to <c>false</c> when the
		/// preference has never been written — fresh installs hide the "+ Add"
		/// button on the Edit Position page, matching the typical setup where
		/// holders are managed centrally via the Unity API. Operators who want
		/// to create holders directly on a device can turn this on in Settings.
		/// </summary>
		public bool IsAddPositionHolderEnabled
		{
			get
			{
				try
				{
					var raw = Preferences.Get(ADD_POSITION_HOLDER_ENABLED_KEY, string.Empty);
					if (string.IsNullOrEmpty(raw)) return false;
					return bool.TryParse(raw, out var value) && value;
				}
				catch (Exception ex)
				{
					// If Preferences is unavailable, fail safe by hiding the
					// button — matches the default for fresh installs and
					// keeps behaviour consistent across a broken-prefs edge case.
					Logger.Warning(ex, "Failed to read add-position-holder toggle — defaulting to disabled");
					return false;
				}
			}
		}

		public void SetAddPositionHolderEnabled(bool enabled)
		{
			try
			{
				Preferences.Set(ADD_POSITION_HOLDER_ENABLED_KEY, enabled ? "true" : "false");
				Logger.Information("Add-position-holder button {State}", enabled ? "ENABLED" : "DISABLED");
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Failed to save add-position-holder toggle");
			}
		}

		// =================================================================
		// Welcome-email-on-registration toggle
		// =================================================================

		/// <summary>
		/// Reads the toggle from Preferences. Defaults to <c>false</c> when
		/// the preference has never been written — fresh installs do not
		/// send registration-time emails until an operator opts in. The
		/// per-recipient send path in <c>AttendanceService</c> is gated on
		/// this read, so flipping the value in Settings takes effect on
		/// the next registration action without an app restart.
		/// </summary>
		public bool IsWelcomeEmailOnRegistrationEnabled
		{
			get
			{
				try
				{
					var raw = Preferences.Get(WELCOME_EMAIL_ENABLED_KEY, string.Empty);
					if (string.IsNullOrEmpty(raw)) return false;
					return bool.TryParse(raw, out var value) && value;
				}
				catch (Exception ex)
				{
					// If Preferences is unavailable, fail safe by treating
					// the feature as off — matches the default for fresh
					// installs and keeps the no-surprise-emails invariant
					// if the prefs store is broken.
					Logger.Warning(ex, "Failed to read welcome-email toggle — defaulting to disabled");
					return false;
				}
			}
		}

		public void SetWelcomeEmailOnRegistrationEnabled(bool enabled)
		{
			try
			{
				Preferences.Set(WELCOME_EMAIL_ENABLED_KEY, enabled ? "true" : "false");
				Logger.Information("Welcome-email-on-registration {State}", enabled ? "ENABLED" : "DISABLED");
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Failed to save welcome-email toggle");
			}
		}

		// =================================================================
		// Device Label (Better Stack / Serilog enricher)
		// =================================================================

		/// <summary>
		/// Returns the user-set label if any, otherwise an auto-generated
		/// default that's still distinct enough to tell two devices apart in
		/// the Better Stack live tail. The auto-default deliberately includes
		/// <c>DeviceInfo.VersionString</c> so two physically identical Android
		/// tablets on different OS versions (e.g. Android 15 vs 16) sort apart
		/// without any configuration. <c>Environment.MachineName</c> is used
		/// only on desktop, where it is meaningful — on Android it returns
		/// <c>"localhost"</c> and on iOS it returns a sandbox hostname.
		/// </summary>
		public string DeviceLabel
		{
			get
			{
				try
				{
					var stored = Preferences.Get(DEVICE_LABEL_KEY, string.Empty);
					if (!string.IsNullOrWhiteSpace(stored))
						return stored;
				}
				catch (Exception ex)
				{
					// Preferences unavailable — fall through to the platform default.
					Logger.Warning(ex, "Failed to read device label from Preferences — using auto-default");
				}

				return BuildDefaultDeviceLabel();
			}
		}

		public void SetDeviceLabel(string? label)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(label))
				{
					Preferences.Remove(DEVICE_LABEL_KEY);
					Logger.Information("Device label cleared — will use auto-default");
				}
				else
				{
					var trimmed = label.Trim();
					Preferences.Set(DEVICE_LABEL_KEY, trimmed);
					Logger.Information("Device label set to {DeviceLabel}", trimmed);
				}
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Failed to save device label");
			}
		}

		/// <summary>
		/// Computes a sensible cross-platform default device label. Never
		/// returns <c>"localhost"</c> — on mobile we always synthesise from
		/// <see cref="DeviceInfo"/>, on desktop we use the OS host name which
		/// is what the operator already recognises.
		/// </summary>
		private static string BuildDefaultDeviceLabel()
		{
			try
			{
				var platform = DeviceInfo.Platform;

				if (platform == DevicePlatform.WinUI || platform == DevicePlatform.MacCatalyst)
				{
					// Desktop: MachineName is meaningful (e.g. "DESK-OFFICE-01").
					var machine = Environment.MachineName;
					if (!string.IsNullOrWhiteSpace(machine) &&
						!string.Equals(machine, "localhost", StringComparison.OrdinalIgnoreCase))
					{
						return machine;
					}
					// Extremely unusual — fall through to the model-based label.
				}

				// Mobile (Android, iOS) and the desktop fallback above.
				// Combine manufacturer, model and OS version. The version is
				// the bit that lets you tell apart two otherwise-identical
				// tablets on different Android releases.
				var manufacturer = (DeviceInfo.Manufacturer ?? string.Empty).Trim();
				var model = (DeviceInfo.Model ?? string.Empty).Trim();
				var osName = platform.ToString();         // "Android", "iOS", "WinUI", "MacCatalyst"
				var osVer = (DeviceInfo.VersionString ?? string.Empty).Trim();

				// Avoid repeating the manufacturer when it's already in the model
				// string (Samsung tends to do this; "Samsung SM-G991B" vs "SM-G991B").
				var hardware = !string.IsNullOrEmpty(manufacturer) &&
							   !model.StartsWith(manufacturer, StringComparison.OrdinalIgnoreCase)
					? $"{manufacturer} {model}".Trim()
					: model;

				if (string.IsNullOrWhiteSpace(hardware))
					hardware = "Device";

				return string.IsNullOrWhiteSpace(osVer)
					? $"{hardware} ({osName})"
					: $"{hardware} ({osName} {osVer})";
			}
			catch
			{
				// Anything genuinely unexpected — return something non-empty
				// rather than letting the enricher write a blank.
				return "UnknownDevice";
			}
		}

		/// <summary>
		/// Reads the toggle from Preferences. Defaults to <c>true</c> when
		/// the preference has never been written — fresh installs do not
		/// send acceptance-confirmation email until an operator opts in.
		/// The per-recipient send path in <c>ComplianceService</c> is gated
		/// on this read, so flipping the value in Settings takes effect on
		/// the next acceptance action without an app restart.
		/// </summary>
		public bool IsComplianceAcceptanceEmailEnabled
		{
			get
			{
				try
				{
					var raw = Preferences.Get(COMPLIANCE_ACCEPTANCE_EMAIL_ENABLED_KEY, string.Empty);
					if (string.IsNullOrEmpty(raw)) return true;
					return bool.TryParse(raw, out var value) && value;
				}
				catch (Exception ex)
				{
					// If Preferences is unavailable, fail safe by treating
					// the feature as off — matches the default for fresh
					// installs and keeps the no-surprise-emails invariant
					// if the prefs store is broken.
					Logger.Warning(ex, "Failed to read compliance-acceptance-email toggle — defaulting to disabled");
					return false;
				}
			}
		}

		public void SetComplianceAcceptanceEmailEnabled(bool enabled)
		{
			try
			{
				Preferences.Set(COMPLIANCE_ACCEPTANCE_EMAIL_ENABLED_KEY, enabled ? "true" : "false");
				Logger.Information("Compliance-acceptance-email {State}", enabled ? "ENABLED" : "DISABLED");
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Failed to save compliance-acceptance-email toggle");
			}
		}
	}
}
