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

		private const string SMTP_PASSWORD_KEY = "smtp_password";
		private const string UNITY_API_KEY = "unity_api_key";
		private const string UNITY_ACTIVE_MEETING_KEY = "unity_active_meeting_id";
		private const string BETTERSTACK_SOURCE_TOKEN_KEY = "betterstack_source_token";

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
	}
}