namespace TheBleedingDeacons.Intergroup.Register.Models
{
    public class UnityConfiguration
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Validates the Unity API configuration.
        /// </summary>
        /// <returns>True if configuration is valid, false otherwise.</returns>
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(BaseUrl) &&
                   !string.IsNullOrWhiteSpace(ApiKey) &&
                   Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        /// <summary>
        /// Creates a copy with the API key masked for logging.
        /// </summary>
        public UnityConfiguration ToLogSafe()
        {
            return new UnityConfiguration
            {
                BaseUrl = BaseUrl,
                ApiKey = string.IsNullOrEmpty(ApiKey) ? "" : "***"
            };
        }
    }
}
