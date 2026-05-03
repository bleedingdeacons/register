using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
    /// <summary>
    /// Default <see cref="IScrutinyClient"/> implementation, talking to the
    /// Scrutiny plugin's REST endpoints over HTTP. See the interface docs
    /// for the route → method mapping.
    ///
    /// <para><b>Base URL.</b> The Scrutiny plugin lives on the same
    /// WordPress site as the Unity API, so we deliberately reuse
    /// <see cref="UnityConfiguration.BaseUrl"/> rather than introducing a
    /// second user-configurable setting that would inevitably drift. If
    /// the two ever need to point at different hosts, this is the place
    /// to split them.</para>
    ///
    /// <para><b>HttpClient.</b> Injected as the unkeyed singleton from
    /// <c>MauiProgram</c>, which is the platform-native handler. Scrutiny
    /// shares its host with Unity, and that host sits behind a TLS-
    /// fingerprinting edge WAF — using the same client keeps Scrutiny
    /// requests indistinguishable from Unity requests at the WAF.</para>
    ///
    /// <para><b>Auth.</b> Scrutiny's privacy-policy routes are
    /// deliberately public (the plugin registers them with
    /// <c>permission_callback =&gt; '__return_true'</c> because privacy
    /// policies are meant to be readable by anyone). We do not attach
    /// any Authorization header here — adding one would be harmless but
    /// misleading.</para>
    ///
    /// <para><b>Lifetime.</b> Singleton. The client is stateless beyond
    /// its dependencies, and the configuration service it reads from
    /// caches its own results, so there's no benefit to a per-call
    /// factory like the one used for <c>UnityRestSharp</c>.</para>
    /// </summary>
    public sealed class ScrutinyClient : IScrutinyClient
    {
        private static readonly ILogger Logger = AppLogger.ForContext<ScrutinyClient>();

        // The route prefix registered by Scrutiny\Rest\PrivacyPolicyController.
        // Kept as a constant rather than threaded through config because the
        // namespace is part of the plugin's public contract — changing it
        // upstream is a breaking change that warrants a code update here too.
        private const string RoutePrefix = "/wp-json/scrutiny/v1/privacy-policies";

        // System.Text.Json options shared across every call. Reusing a
        // single instance is the documented best practice — JsonSerializer
        // caches metadata against the options object, so a fresh options
        // instance per call defeats that cache.
        //
        // PropertyNameCaseInsensitive is defensive: the server emits exact
        // snake_case and our [JsonPropertyName] attributes match exactly,
        // so this isn't strictly required, but it costs us nothing and
        // makes the deserialisation tolerant of minor server-side casing
        // changes.
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly HttpClient _httpClient;
        private readonly IConfigurationService _configurationService;

        public ScrutinyClient(
            HttpClient httpClient,
            IConfigurationService configurationService)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        }

        public async Task<IReadOnlyList<PrivacyPolicy>> GetPrivacyPoliciesAsync(
            bool activeOnly = false,
            CancellationToken cancellationToken = default)
        {
            // The query string must use lowercase "true"/"false" because
            // WordPress's rest_sanitize_boolean is case-sensitive against
            // the literal strings "true"/"1"/"yes"/"on" — anything else
            // is coerced to false. The .NET default Boolean.ToString()
            // returns "True", which would silently disable the filter.
            var path = activeOnly
                ? $"{RoutePrefix}?active={bool.TrueString.ToLowerInvariant()}"
                : RoutePrefix;

            var url = await BuildUrlAsync(path).ConfigureAwait(false);

            Logger.Debug("Scrutiny: GET {Url}", url);

            using var response = await _httpClient
                .GetAsync(url, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            // ReadFromJsonAsync handles the empty-array case correctly —
            // an empty body would throw, but the server always emits at
            // least "[]" for the collection routes.
            var policies = await response.Content
                .ReadFromJsonAsync<List<PrivacyPolicy>>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return policies ?? new List<PrivacyPolicy>();
        }

        public async Task<PrivacyPolicy?> GetActivePrivacyPolicyAsync(
            CancellationToken cancellationToken = default)
        {
            var url = await BuildUrlAsync($"{RoutePrefix}/active").ConfigureAwait(false);

            Logger.Debug("Scrutiny: GET {Url}", url);

            using var response = await _httpClient
                .GetAsync(url, cancellationToken)
                .ConfigureAwait(false);

            // 404 is the documented "no active policy" signal. Surface
            // it as null rather than throwing — see interface docs for
            // why callers prefer that shape.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Logger.Information("Scrutiny: no active privacy policy is published");
                return null;
            }

            response.EnsureSuccessStatusCode();

            return await response.Content
                .ReadFromJsonAsync<PrivacyPolicy>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<PrivacyPolicy?> GetPrivacyPolicyAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            if (id <= 0)
                throw new ArgumentOutOfRangeException(nameof(id), id, "Privacy policy ID must be a positive integer.");

            // Path segment, not a query parameter — the upstream route
            // is /(?P<id>\d+). Using InvariantCulture so a thread with
            // an unusual NumberFormatInfo can't sneak digit-group
            // separators into the URL.
            var url = await BuildUrlAsync(
                $"{RoutePrefix}/{id.ToString(CultureInfo.InvariantCulture)}")
                .ConfigureAwait(false);

            Logger.Debug("Scrutiny: GET {Url}", url);

            using var response = await _httpClient
                .GetAsync(url, cancellationToken)
                .ConfigureAwait(false);

            // 404 covers three cases on the server side: unknown ID,
            // wrong post type, and unpublished draft. From the caller's
            // perspective these are indistinguishable — collapse them
            // all to null.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Logger.Information("Scrutiny: privacy policy {Id} not found", id);
                return null;
            }

            response.EnsureSuccessStatusCode();

            return await response.Content
                .ReadFromJsonAsync<PrivacyPolicy>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Builds the absolute request URL by joining the configured
        /// base URL to the supplied path. Throws if the base URL has
        /// not been configured — we'd rather fail loudly than silently
        /// fire off a request to a relative URI that the platform
        /// handler resolves unpredictably.
        /// </summary>
        private async Task<string> BuildUrlAsync(string path)
        {
            var unityConfig = await _configurationService.LoadUnityConfigurationAsync().ConfigureAwait(false);
            var baseUrl = unityConfig.BaseUrl;

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new InvalidOperationException(
                    "Scrutiny base URL is not configured. Set UnitySettings.BaseUrl in the integrations settings — " +
                    "Scrutiny lives on the same WordPress site as Unity and shares its base URL.");
            }

            // TrimEnd on the base, the path always starts with '/', so
            // the join is a single concatenation with no double-slash
            // and no missing-slash failure modes.
            return baseUrl.TrimEnd('/') + path;
        }
    }
}
