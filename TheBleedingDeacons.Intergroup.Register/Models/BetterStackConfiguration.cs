namespace TheBleedingDeacons.Intergroup.Register.Models
{
    public class BetterStackConfiguration
    {
        private string _endpoint = string.Empty;

        public string SourceToken { get; set; } = string.Empty;

        /// <summary>
        /// Better Stack's HTTP ingest endpoint.
        /// </summary>
        /// <remarks>
        /// <para>Normalised on the way in: a value with no scheme gets
        /// <c>https://</c>. That is not cosmetic, it is the fix for a bug that
        /// silently disabled log shipping entirely.</para>
        ///
        /// <para>Better Stack's own dashboard shows the ingest address as a bare
        /// hostname — <c>sNNNNNN.eu-central-1a.betterstackdata.com</c> — so that
        /// is what gets pasted into configuration, and it is what
        /// devsettings.json has always held. But <see cref="IsValid"/> requires an
        /// absolute http/https URI, and <c>Uri.TryCreate</c> refuses a bare
        /// hostname. So <see cref="IsValid"/> returned false, the logger
        /// controller took its "config invalid or cleared" branch, and the durable
        /// sink was never attached — meaning dev builds shipped no log events to
        /// Better Stack at all. Nothing threw and nothing was logged as an error,
        /// because not having a sink configured is a legitimate state; it just
        /// happened to be the wrong one.</para>
        ///
        /// <para>Normalising in the setter rather than at the point of use is
        /// deliberate: the value arrives from an embedded JSON file, from a saved
        /// settings file, and from the settings page, and a fix applied at only
        /// some of those would leave the same trap for the next path added.</para>
        /// </remarks>
        public string Endpoint
        {
            get => _endpoint;
            set => _endpoint = NormaliseEndpoint(value);
        }

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

        /// <summary>
        /// Gives a scheme-less endpoint the <c>https://</c> it needs to parse as
        /// an absolute URI. An empty or whitespace value stays empty — that means
        /// "not configured", which is a supported state and must not become a bare
        /// "https://".
        /// </summary>
        private static string NormaliseEndpoint(string? value)
        {
            var endpoint = (value ?? string.Empty).Trim();

            if (endpoint.Length == 0)
            {
                return string.Empty;
            }

            // Checking for "://" rather than a known scheme prefix keeps an
            // explicit http:// working unchanged, and avoids mangling anything
            // that already carries a scheme we don't recognise.
            return endpoint.Contains("://", StringComparison.Ordinal)
                ? endpoint
                : "https://" + endpoint;
        }
    }
}
