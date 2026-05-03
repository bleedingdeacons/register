using Microsoft.Extensions.Configuration;
using Serilog;
using System.Reflection;
using System.Text.Json;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
	public class ConfigurationService : IConfigurationService
	{
		private static readonly ILogger Logger = AppLogger.ForContext<ConfigurationService>();

		private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
		private const string COMPLIANCE_ACCEPTANCE_EMAIL_ENABLED_KEY = "compliance_acceptance_email_enabled";
		private const string SMTP_PASSWORD_KEY = "smtp_password";
		private const string UNITY_API_KEY = "unity_api_key";
		private const string UNITY_ACTIVE_MEETING_KEY = "unity_active_meeting_id";
		private const string BETTERSTACK_SOURCE_TOKEN_KEY = "betterstack_source_token";
		private const string REGISTRATION_LOG_ENABLED_KEY = "registration_log_enabled";
		private const string AUTO_REGISTER_POSITIONS_KEY = "auto_register_positions_on_group";
		private const string SINGLE_GSR_SHORTCUT_KEY = "single_gsr_shortcut_enabled";
		private const string COMPLIANCE_LOG_ENABLED_KEY = "compliance_log_enabled";
		private const string WELCOME_EMAIL_ENABLED_KEY = "welcome_email_on_registration_enabled";
		private const string DEVICE_LABEL_KEY = "device_label";

#if USE_DEV_CREDENTIALS
		private const string DEV_CREDENTIALS_RESOURCE =
			"TheBleedingDeacons.Intergroup.Register.devsettings.json";
#endif

		private readonly IConfiguration _configuration;
		private readonly string _configFilePath;
		private readonly string _unityConfigFilePath;
		private readonly string _betterStackConfigFilePath;

		private SmtpConfiguration? _cachedSmtpConfig;
		private UnityConfiguration? _cachedUnityConfig;
		private BetterStackConfiguration? _cachedBetterStackConfig;

		public ConfigurationService()
		{
			var builder = new ConfigurationBuilder();

			// Load embedded appsettings.json
			var assembly = Assembly.GetExecutingAssembly();
			var stream = assembly.GetManifestResourceStream("TheBleedingDeacons.Intergroup.Register.appsettings.json");
			if (stream != null)
			{
				builder.AddJsonStream(stream);
			}

			// Load user-specific config file from app data
			_configFilePath = Path.Combine(FileSystem.AppDataDirectory, "mailsettings.json");
			_unityConfigFilePath = Path.Combine(FileSystem.AppDataDirectory, "unitysettings.json");
			_betterStackConfigFilePath = Path.Combine(FileSystem.AppDataDirectory, "betterstacksettings.json");
			if (File.Exists(_configFilePath))
			{
				builder.AddJsonFile(_configFilePath, optional: true, reloadOnChange: false);
			}

			_configuration = builder.Build();
		}

		// =================================================================
		// SMTP
		// =================================================================

		public SmtpConfiguration GetSmtpConfiguration()
		{
			if (_cachedSmtpConfig != null)
				return _cachedSmtpConfig;

#if USE_DEV_CREDENTIALS
			var (_, password) = LoadEmbeddedDevCredentials(
				"SmtpSettings", "Host", "Password");
#else
			var password = GetSecretSync(SMTP_PASSWORD_KEY, "SMTP password");
#endif

			_cachedSmtpConfig = BuildSmtpConfiguration(password);
			return _cachedSmtpConfig;
		}

		public async Task SaveSmtpConfigurationAsync(SmtpConfiguration config)
		{
			await SaveSecretAsync(SMTP_PASSWORD_KEY, config.Password, "SMTP password");
			await SaveJsonSettingsAsync(_configFilePath, "SmtpSettings", new
			{
				config.Host,
				config.Port,
				config.Username,
				config.EnableSsl,
				config.FromDisplayName,
				config.TimeoutSeconds
			});
			_cachedSmtpConfig = config;
		}

		public async Task<SmtpConfiguration> LoadSmtpConfigurationAsync()
		{
#if USE_DEV_CREDENTIALS
			var (_, password) = LoadEmbeddedDevCredentials(
				"SmtpSettings", "Host", "Password");
#else
			var password = await GetSecretAsync(SMTP_PASSWORD_KEY, "SMTP password");
#endif

			_cachedSmtpConfig = BuildSmtpConfiguration(password);
			return _cachedSmtpConfig;
		}

		/// <summary>
		/// Binds the SmtpSettings section straight onto a new configuration
		/// object and fills in the password (which is stored separately in
		/// SecureStorage). IConfiguration's typed binding handles Port/EnableSsl/
		/// TimeoutSeconds conversion, so no manual TryParse is needed.
		/// </summary>
		private SmtpConfiguration BuildSmtpConfiguration(string password)
		{
			var config = new SmtpConfiguration();
			_configuration.GetSection("SmtpSettings").Bind(config);
			config.Password = password;
			return config;
		}

		// =================================================================
		// Unity
		// =================================================================

		public async Task SaveUnityConfigurationAsync(UnityConfiguration config)
		{
			await SaveSecretAsync(UNITY_API_KEY, config.ApiKey, "Unity API key");
			await SaveJsonSettingsAsync(_unityConfigFilePath, "UnitySettings", new { config.BaseUrl });
			_cachedUnityConfig = config;
		}

		public async Task<UnityConfiguration> LoadUnityConfigurationAsync()
		{
			string baseUrl;
			string apiKey;

#if USE_DEV_CREDENTIALS
			(baseUrl, apiKey) = LoadEmbeddedDevCredentials(
				"UnitySettings", "BaseUrl", "ApiKey");
#else
			baseUrl = await ReadJsonPropertyAsync(_unityConfigFilePath, "UnitySettings", "BaseUrl", "Unity settings");
			apiKey = await GetSecretAsync(UNITY_API_KEY, "Unity API key");
#endif

			int? activeIntergroupMeetingId = null;
			try
			{
				var raw = Preferences.Get(UNITY_ACTIVE_MEETING_KEY, string.Empty);
				if (int.TryParse(raw, out var parsedId) && parsedId > 0)
					activeIntergroupMeetingId = parsedId;
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Failed to load active intergroup meeting ID from Preferences");
			}

			_cachedUnityConfig = new UnityConfiguration
			{
				BaseUrl = baseUrl,
				ApiKey = apiKey,
				ActiveIntergroupMeetingId = activeIntergroupMeetingId,
			};

			return _cachedUnityConfig;
		}

		public async Task SaveActiveIntergroupMeetingAsync(int? meetingId)
		{
			try
			{
				if (meetingId.HasValue && meetingId.Value > 0)
					Preferences.Set(UNITY_ACTIVE_MEETING_KEY, meetingId.Value.ToString());
				else
					Preferences.Remove(UNITY_ACTIVE_MEETING_KEY);
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Failed to save active intergroup meeting ID to Preferences");
			}

			if (_cachedUnityConfig != null)
				_cachedUnityConfig.ActiveIntergroupMeetingId = meetingId;

			Logger.Information("Active intergroup meeting set to {MeetingId}", meetingId?.ToString() ?? "none");
		}

		// =================================================================
		// Better Stack
		// =================================================================

		public BetterStackConfiguration GetBetterStackConfiguration()
		{
			if (_cachedBetterStackConfig != null)
				return _cachedBetterStackConfig;

#if USE_DEV_CREDENTIALS
			var (endpoint, sourceToken) = LoadEmbeddedDevCredentials(
				"BetterStack", "Endpoint", "SourceToken");
#else
			var endpoint = ReadJsonProperty(_betterStackConfigFilePath, "BetterStack", "Endpoint", "Better Stack settings");

			// Fall back to embedded appsettings.json
			if (string.IsNullOrWhiteSpace(endpoint))
				endpoint = _configuration.GetSection("BetterStack")["Endpoint"] ?? "";

			var sourceToken = GetSecretSync(BETTERSTACK_SOURCE_TOKEN_KEY, "Better Stack source token");
#endif

			_cachedBetterStackConfig = new BetterStackConfiguration
			{
				Endpoint = endpoint,
				SourceToken = sourceToken
			};

			return _cachedBetterStackConfig;
		}

		public async Task SaveBetterStackConfigurationAsync(BetterStackConfiguration config)
		{
			await SaveSecretAsync(BETTERSTACK_SOURCE_TOKEN_KEY, config.SourceToken, "Better Stack source token");
			await SaveJsonSettingsAsync(_betterStackConfigFilePath, "BetterStack", new { config.Endpoint });
			_cachedBetterStackConfig = config;
		}

		public async Task<BetterStackConfiguration> LoadBetterStackConfigurationAsync()
		{
			string endpoint;
			string sourceToken;

#if USE_DEV_CREDENTIALS
			(endpoint, sourceToken) = LoadEmbeddedDevCredentials(
				"BetterStack", "Endpoint", "SourceToken");
#else
			endpoint = await ReadJsonPropertyAsync(_betterStackConfigFilePath, "BetterStack", "Endpoint", "Better Stack settings");

			if (string.IsNullOrWhiteSpace(endpoint))
				endpoint = _configuration.GetSection("BetterStack")["Endpoint"] ?? "";

			sourceToken = await GetSecretAsync(BETTERSTACK_SOURCE_TOKEN_KEY, "Better Stack source token");
#endif

			_cachedBetterStackConfig = new BetterStackConfiguration
			{
				Endpoint = endpoint,
				SourceToken = sourceToken
			};

			return _cachedBetterStackConfig;
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
					return bool.TryParse(raw, out var value) ? value : true;
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
					return bool.TryParse(raw, out var value) ? value : true;
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
					return bool.TryParse(raw, out var value) ? value : true;
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
					return bool.TryParse(raw, out var value) ? value : false;
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
					return bool.TryParse(raw, out var value) ? value : false;
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

		// =================================================================
		// Shared helpers — JSON file I/O
		// =================================================================

		/// <summary>
		/// Reads a single property from a JSON settings file (sync).
		/// Returns empty string if the file doesn't exist or the property is missing.
		/// </summary>
		private string ReadJsonProperty(string filePath, string sectionName, string propertyName, string description)
		{
			if (!File.Exists(filePath))
				return "";

			try
			{
				var json = File.ReadAllText(filePath);
				return ExtractProperty(json, sectionName, propertyName);
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Failed to load {Description} from file", description);
				return "";
			}
		}

		/// <summary>
		/// Reads a single property from a JSON settings file (async).
		/// Returns empty string if the file doesn't exist or the property is missing.
		/// </summary>
		private async Task<string> ReadJsonPropertyAsync(string filePath, string sectionName, string propertyName, string description)
		{
			if (!File.Exists(filePath))
				return "";

			try
			{
				var json = await File.ReadAllTextAsync(filePath);
				return ExtractProperty(json, sectionName, propertyName);
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Failed to load {Description} from file", description);
				return "";
			}
		}

		private static string ExtractProperty(string json, string sectionName, string propertyName)
		{
			var doc = JsonDocument.Parse(json);
			if (doc.RootElement.TryGetProperty(sectionName, out var section) &&
				section.TryGetProperty(propertyName, out var prop))
			{
				return prop.GetString() ?? "";
			}
			return "";
		}

		/// <summary>
		/// Serialises a settings object under a named section and writes it to a JSON file.
		/// </summary>
		private static async Task SaveJsonSettingsAsync(string filePath, string sectionName, object settings)
		{
			var wrapper = new Dictionary<string, object> { [sectionName] = settings };
			var json = JsonSerializer.Serialize(wrapper, WriteOptions);
			await File.WriteAllTextAsync(filePath, json);
		}

#if USE_DEV_CREDENTIALS
		/// <summary>
		/// Reads two properties from a named section of the embedded
		/// devsettings.json resource. Only present in builds where
		/// USE_DEV_CREDENTIALS is defined (i.e. any build that isn't run
		/// with -p:UseDevCredentials=false). Returns empty strings on
		/// failure so callers produce an "invalid" config and skip setup
		/// rather than throw on startup.
		/// </summary>
		private static (string First, string Second) LoadEmbeddedDevCredentials(
			string sectionName, string firstProperty, string secondProperty)
		{
			var assembly = Assembly.GetExecutingAssembly();
			using var stream = assembly.GetManifestResourceStream(DEV_CREDENTIALS_RESOURCE);
			if (stream == null)
			{
				Logger.Error(
					"Embedded resource {Resource} not found. Dev credentials for {Section} will be empty. " +
					"Ensure devsettings.json exists in the project root and " +
					"UseDevCredentials=true when building.",
					DEV_CREDENTIALS_RESOURCE, sectionName);
				return ("", "");
			}

			try
			{
				using var doc = JsonDocument.Parse(stream);
				if (doc.RootElement.TryGetProperty(sectionName, out var section))
				{
					var first = section.TryGetProperty(firstProperty, out var a) ? a.GetString() ?? "" : "";
					var second = section.TryGetProperty(secondProperty, out var b) ? b.GetString() ?? "" : "";
					return (first, second);
				}

				Logger.Warning(
					"Section {Section} missing from embedded {Resource}",
					sectionName, DEV_CREDENTIALS_RESOURCE);
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Failed to parse embedded {Resource}", DEV_CREDENTIALS_RESOURCE);
			}

			return ("", "");
		}
#endif

		// =================================================================
		// Shared helpers — SecureStorage
		// =================================================================

		/// <summary>
		/// Reads a secret from SecureStorage (sync-safe). Uses Task.Run to
		/// hop off the calling SynchronizationContext, avoiding deadlock when
		/// called from the MAUI UI thread. Returns empty string on failure.
		/// </summary>
		private static string GetSecretSync(string key, string description)
		{
			try
			{
				return Task.Run(() => SecureStorage.GetAsync(key)).GetAwaiter().GetResult() ?? "";
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "SecureStorage unavailable for {Description}", description);
				return "";
			}
		}

		/// <summary>
		/// Reads a secret from SecureStorage (async). Returns empty string on failure.
		/// </summary>
		private static async Task<string> GetSecretAsync(string key, string description)
		{
			try
			{
				return await SecureStorage.GetAsync(key) ?? "";
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "SecureStorage unavailable for {Description}", description);
				return "";
			}
		}

		/// <summary>
		/// Writes a secret to SecureStorage. Logs a warning on failure.
		/// </summary>
		private static async Task SaveSecretAsync(string key, string value, string description)
		{
			try
			{
				await SecureStorage.SetAsync(key, value);
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "SecureStorage unavailable for {Description}", description);
			}
		}

		/// <summary>
		/// Reads the toggle from Preferences. Defaults to <c>false</c> when
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
					if (string.IsNullOrEmpty(raw)) return false;
					return bool.TryParse(raw, out var value) ? value : false;
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