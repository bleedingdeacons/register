using System.Text.Json.Serialization;

namespace TheBleedingDeacons.Intergroup.Register.Models
{
    /// <summary>
    /// A privacy policy as exposed by the Scrutiny WordPress plugin's
    /// read-only REST endpoint at <c>/wp-json/scrutiny/v1/privacy-policies</c>.
    ///
    /// <para>The shape matches the JSON returned by
    /// <c>Scrutiny\Rest\PrivacyPolicyController::formatPolicy</c> exactly:
    /// the controller deliberately strips the <c>gdpr-</c> prefix from the
    /// underlying ACF field names and converts kebab-case to snake_case so
    /// the wire format follows REST conventions, and this POCO mirrors that
    /// flat shape rather than the original ACF field structure.</para>
    ///
    /// <para>All fields are populated for every response — the controller
    /// uses empty-string defaults for missing ACF values rather than
    /// emitting null — so the non-nullable string defaults below are safe
    /// even if the upstream policy is partially configured.</para>
    /// </summary>
    public class PrivacyPolicy
    {
        /// <summary>
        /// The WordPress post ID of the policy. Stable across edits to
        /// the policy text but not across re-imports of the site.
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>
        /// The post title — typically the policy's display name (e.g.
        /// "Privacy Policy" or "Member Privacy Notice").
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// The human-readable version string set on the policy (e.g.
        /// "2.1"). Free-form; not parsed.
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// True when the policy is the currently active one. Multiple
        /// policies can in principle be flagged active simultaneously
        /// (the upstream schema doesn't prevent it); the
        /// <c>/privacy-policies/active</c> route returns the most recent.
        /// </summary>
        [JsonPropertyName("active")]
        public bool Active { get; set; }

        /// <summary>
        /// The named contact for data-protection enquiries (e.g.
        /// "Data Protection Officer").
        /// </summary>
        [JsonPropertyName("contact")]
        public string Contact { get; set; } = string.Empty;

        /// <summary>
        /// The email address for data-protection enquiries.
        /// </summary>
        [JsonPropertyName("contact_email")]
        public string ContactEmail { get; set; } = string.Empty;

        /// <summary>
        /// The full policy body as rendered HTML — already passed
        /// through ACF's default formatting (<c>wpautop</c> and
        /// shortcode resolution) on the server, so it can be dropped
        /// straight into a <c>WebView</c> or HTML-rendering label
        /// without further processing.
        /// </summary>
        [JsonPropertyName("policy")]
        public string Policy { get; set; } = string.Empty;

        /// <summary>
        /// ISO-8601 timestamp of the last modification, in UTC. Useful
        /// for cache invalidation and for showing the user when the
        /// policy they're reading was last updated.
        /// </summary>
        [JsonPropertyName("modified")]
        public string Modified { get; set; } = string.Empty;
    }
}
