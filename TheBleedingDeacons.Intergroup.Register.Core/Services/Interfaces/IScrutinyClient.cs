using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Services.Interfaces
{
    /// <summary>
    /// Read-only client for the Scrutiny WordPress plugin's privacy-policy
    /// REST endpoints. Mirrors the three routes registered by
    /// <c>Scrutiny\Rest\PrivacyPolicyController</c>:
    ///
    /// <list type="bullet">
    /// <item><c>GET /scrutiny/v1/privacy-policies</c> →
    ///       <see cref="GetPrivacyPoliciesAsync"/></item>
    /// <item><c>GET /scrutiny/v1/privacy-policies/active</c> →
    ///       <see cref="GetActivePrivacyPolicyAsync"/></item>
    /// <item><c>GET /scrutiny/v1/privacy-policies/{id}</c> →
    ///       <see cref="GetPrivacyPolicyAsync"/></item>
    /// </list>
    ///
    /// <para>The endpoints are public on the server side (no API key is
    /// required), but the implementation still routes through the
    /// platform-native <see cref="HttpClient"/> so requests share the
    /// TLS fingerprint of the OS HTTPS stack — the same workaround
    /// <c>UnityRestSharp</c> uses for the JA3-fingerprinting edge WAF
    /// that fronts the same site.</para>
    /// </summary>
    public interface IScrutinyClient
    {
        /// <summary>
        /// Lists all published privacy policies, newest first. When
        /// <paramref name="activeOnly"/> is true, the server filters
        /// the result to only those flagged as currently active —
        /// cheaper than filtering client-side because the entire body
        /// is JSON-encoded once on the server either way.
        /// </summary>
        /// <param name="activeOnly">
        /// When true, only currently-active policies are returned.
        /// Defaults to false (return everything).
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the in-flight request. Honoured all the way down to
        /// the underlying <see cref="HttpClient"/>.
        /// </param>
        /// <returns>
        /// The collection of policies, possibly empty. Never null.
        /// </returns>
        /// <exception cref="HttpRequestException">
        /// The server returned a non-success status, or the network
        /// transport failed.
        /// </exception>
        /// <exception cref="System.Text.Json.JsonException">
        /// The response body could not be parsed as the expected
        /// privacy-policy JSON shape.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The Scrutiny base URL is not configured.
        /// </exception>
        Task<IReadOnlyList<PrivacyPolicy>> GetPrivacyPoliciesAsync(
            bool activeOnly = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Fetches the single most-recent active privacy policy, or
        /// <c>null</c> if no policy is currently flagged active.
        ///
        /// <para>The upstream route returns 404 when no active policy
        /// exists; we surface that as <c>null</c> rather than throwing,
        /// because callers typically want to render a fallback UI in
        /// that case rather than treat it as an exceptional error.
        /// Other non-success statuses (5xx, network failures) still
        /// throw.</para>
        /// </summary>
        /// <returns>
        /// The active policy, or <c>null</c> if none is published.
        /// </returns>
        /// <exception cref="HttpRequestException">
        /// The server returned a non-404 error status, or the network
        /// transport failed.
        /// </exception>
        /// <exception cref="System.Text.Json.JsonException">
        /// The response body could not be parsed as the expected
        /// privacy-policy JSON shape.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The Scrutiny base URL is not configured.
        /// </exception>
        Task<PrivacyPolicy?> GetActivePrivacyPolicyAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Fetches a single privacy policy by WordPress post ID, or
        /// <c>null</c> if no published policy exists with that ID.
        /// </summary>
        /// <param name="id">The WordPress post ID of the policy.</param>
        /// <returns>
        /// The policy, or <c>null</c> if no published policy exists with
        /// that ID. The upstream endpoint returns 404 for unknown IDs,
        /// for the wrong post type, and for unpublished drafts; we
        /// collapse all three into <c>null</c> because from the client's
        /// perspective they're indistinguishable.
        /// </returns>
        /// <exception cref="HttpRequestException">
        /// The server returned a non-404 error status, or the network
        /// transport failed.
        /// </exception>
        /// <exception cref="System.Text.Json.JsonException">
        /// The response body could not be parsed as the expected
        /// privacy-policy JSON shape.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The Scrutiny base URL is not configured.
        /// </exception>
        Task<PrivacyPolicy?> GetPrivacyPolicyAsync(
            int id,
            CancellationToken cancellationToken = default);
    }
}
