namespace TheBleedingDeacons.Intergroup.Register.Models
{
    public class SmtpConfiguration
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FromDisplayName { get; set; } = string.Empty;
        public bool EnableSsl { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 30;
        public int MaxRetries { get; set; } = 10;

        /// <summary>
        /// Validates the SMTP configuration
        /// </summary>
        /// <returns>True if configuration is valid, false otherwise</returns>
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(Host) &&
                   Port > 0 && Port <= 65535 &&
                   !string.IsNullOrWhiteSpace(Username) &&
                   !string.IsNullOrWhiteSpace(Password) &&
                   TimeoutSeconds > 0 &&
                   MaxRetries > 0;
        }

        /// <summary>
        /// Creates a copy of the configuration with sanitized password for logging
        /// </summary>
        /// <returns>SmtpConfiguration with masked password</returns>
        public SmtpConfiguration ToLogSafe()
        {
            return new SmtpConfiguration
            {
                Host = Host,
                Port = Port,
                Username = Username,
                Password = "***",
                FromDisplayName = FromDisplayName,
                EnableSsl = EnableSsl,
                TimeoutSeconds = TimeoutSeconds,
                MaxRetries = MaxRetries
            };
        }
    }
}