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
        private static readonly ILogger Logger = AppLogger.ForContext<ConfigurationService >();

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
            _cachedSmtpConfig = new SmtpConfiguration
            {
                Host = section["Host"] ?? "",
                Port = int.TryParse(section["Port"], out int port) ? port : 587,
                Username = section["Username"] ?? "",
                Password = section["Password"] ?? "",
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
            var settings = new
            {
                SmtpSettings = config
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
            if (File.Exists(_configFilePath))
            {
                var json = await File.ReadAllTextAsync(_configFilePath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<dynamic>(json);
            }

            return GetSmtpConfiguration();
        }
    }
}
