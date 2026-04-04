namespace TheBleedingDeacons.Intergroup.Register.Models
{
    public class BetterStackConfiguration
    {
        public string SourceToken { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>
        /// Validates the Better Stack configuration.
        /// </summary>
        /// <returns>True if configuration is valid, false otherwise.</returns>
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(SourceToken) &&
                   !string.IsNullOrWhiteSpace(Endpoint) &&
                   Uri.TryCreate(Endpoint, UriKind.Absolute, out var parsed) &&
                   (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
        }

        /// <summary>
        /// Creates a copy with sensitive fields masked for logging.
        /// </summary>
        public BetterStackConfiguration ToLogSafe()
        {
            return new BetterStackConfiguration
            {
                SourceToken = string.IsNullOrEmpty(SourceToken) ? "" : "***",
                Endpoint = Endpoint
            };
        }
    }
}
