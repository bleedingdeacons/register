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
        private readonly IConfiguration _configuration;
        private readonly string _configFilePath;
        private SmtpConfiguration? _cachedSmtpConfig;

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
    }
}