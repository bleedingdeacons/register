using Microsoft.Extensions.Configuration;
using Serilog;
using System.Reflection;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
    public class ConfigurationService : IConfigurationService
    {
        private static readonly ILogger Logger = AppLogger.ForContext<ConfigurationService>();

        private const string SMTP_PASSWORD_KEY = "smtp_password";
        private const string UNITY_API_KEY = "unity_api_key";
        private const string UNITY_ACTIVE_MEETING_KEY = "unity_active_meeting_id";
        private readonly IConfiguration _configuration;
        private readonly string _configFilePath;
        private readonly string _unityConfigFilePath;
        private SmtpConfiguration? _cachedSmtpConfig;
        private UnityConfiguration? _cachedUnityConfig;

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
            if (File.Exists(_configFilePath))
            {
                builder.AddJsonFile(_configFilePath, optional: true, reloadOnChange: false);
            }

            _configuration = builder.Build();
        }

        public SmtpConfiguration GetSmtpConfiguration()
        {
            if (_cachedSmtpConfig != null)
                return _cachedSmtpConfig;

            var section = _configuration.GetSection("SmtpSettings");

            // Retrieve password from SecureStorage, fall back to config file
            string password;
            try
            {
                password = SecureStorage.GetAsync(SMTP_PASSWORD_KEY).GetAwaiter().GetResult() ?? section["Password"] ?? "";
            }
            catch
            {
                // SecureStorage may not be available on all platforms during testing
                password = section["Password"] ?? "";
            }

            _cachedSmtpConfig = new SmtpConfiguration
            {
                Host = section["Host"] ?? "",
                Port = int.TryParse(section["Port"], out int port) ? port : 587,
                Username = section["Username"] ?? "",
                Password = password,
                EnableSsl = bool.TryParse(section["EnableSsl"], out bool ssl) ? ssl : true,
                FromDisplayName = section["FromDisplayName"] ?? "",
                TimeoutSeconds = int.TryParse(section["TimeoutSeconds"], out int timeout) ? timeout : 30
            };

            return _cachedSmtpConfig;
        }

        public void UpdateSmtpConfiguration(SmtpConfiguration config)
        {
            _cachedSmtpConfig = config;
        }

        public async Task SaveSmtpConfigurationAsync(SmtpConfiguration config)
        {
            // Store password securely
            try
            {
                await SecureStorage.SetAsync(SMTP_PASSWORD_KEY, config.Password);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "SecureStorage unavailable, password will be stored in config file");
            }

            // Save non-sensitive settings to JSON (password excluded)
            var settings = new
            {
                SmtpSettings = new
                {
                    config.Host,
                    config.Port,
                    config.Username,
                    config.EnableSsl,
                    config.FromDisplayName,
                    config.TimeoutSeconds,
                    config.MaxRetries
                }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(_configFilePath, json);
            _cachedSmtpConfig = config;
        }

        public async Task<SmtpConfiguration> LoadSmtpConfigurationAsync()
        {
            // Clear cache to force a fresh load
            _cachedSmtpConfig = null;
            return await Task.FromResult(GetSmtpConfiguration());
        }

        public UnityConfiguration GetUnityConfiguration()
        {
            if (_cachedUnityConfig != null)
                return _cachedUnityConfig;

            string baseUrl = "";

            if (File.Exists(_unityConfigFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_unityConfigFilePath);
                    var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("UnitySettings", out var section))
                    {
                        if (section.TryGetProperty("BaseUrl", out var urlProp))
                            baseUrl = urlProp.GetString() ?? "";
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Failed to load Unity settings from file");
                }
            }

            _cachedUnityConfig = new UnityConfiguration
            {
                BaseUrl = baseUrl,
                ApiKey = "",
            };

            return _cachedUnityConfig;
        }

        public async Task SaveUnityConfigurationAsync(UnityConfiguration config)
        {
            // Store API key securely
            try
            {
                await SecureStorage.SetAsync(UNITY_API_KEY, config.ApiKey);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "SecureStorage unavailable for Unity API key");
            }

            // Save non-sensitive settings to JSON (API key excluded)
            var settings = new
            {
                UnitySettings = new
                {
                    config.BaseUrl,
                }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(_unityConfigFilePath, json);
            _cachedUnityConfig = config;
        }

        public async Task<UnityConfiguration> LoadUnityConfigurationAsync()
        {
            string baseUrl = "";
            string apiKey = "";

            if (File.Exists(_unityConfigFilePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_unityConfigFilePath);
                    var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("UnitySettings", out var section))
                    {
                        if (section.TryGetProperty("BaseUrl", out var urlProp))
                            baseUrl = urlProp.GetString() ?? "";
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Failed to load Unity settings from file");
                }
            }

            try
            {
                apiKey = await SecureStorage.GetAsync(UNITY_API_KEY) ?? "";
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "SecureStorage unavailable for Unity API key");
            }

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

            // Keep the in-memory cache consistent
            if (_cachedUnityConfig != null)
                _cachedUnityConfig.ActiveIntergroupMeetingId = meetingId;

            Logger.Information("Active intergroup meeting set to {MeetingId}", meetingId?.ToString() ?? "none");
        }
    }
}