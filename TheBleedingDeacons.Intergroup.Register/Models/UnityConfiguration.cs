namespace TheBleedingDeacons.Intergroup.Register.Models
{
    public class UnityConfiguration
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// The ID of the active intergroup meeting for the current session.
        /// Selected on the Start of Meeting page before each register session.
        /// </summary>
        public int? ActiveIntergroupMeetingId { get; set; }

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
