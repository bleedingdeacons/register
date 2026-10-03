using System.Text.Json.Serialization;

namespace TheBleedingDeacons.Intergroup.Register.Models
{
	/// <summary>
	/// A privacy policy as cached on-device, populated by the sync stage
	/// from the Scrutiny <c>/privacy-policies/active</c> endpoint and read
	/// from cache by the registration flow.
	///
	/// <para>The cache carries the audit-trail fields (Id, Version,
	/// Modified) that we need to record against each acceptance, plus
	/// the <c>Policy</c> HTML body itself. The body is what the consent
	/// popup displays to the user and what the compliance acceptance
	/// email quotes — Scrutiny is the single source of truth for both
	/// the on-screen wording and the audit trail.</para>
	///
	/// <para>The <see cref="CachedAt"/> field is local-only — Scrutiny
	/// doesn't emit it. It records when the cache was last refreshed,
	/// purely so the Settings page can show the operator how stale the
	/// cached value is when they're working offline. It is not part of
	/// the upstream wire format.</para>
	///
	/// <para>Stored as a single JSON blob in <c>Preferences</c>, keyed by
	/// <c>scrutiny.cachedPolicy</c>. JSON over a struct of separate keys
	/// because (a) it's atomic — the whole record is updated in a single
	/// <c>Preferences.Set</c> call, no half-written cache after a crash —
	/// and (b) adding fields later doesn't require a migration.</para>
	/// </summary>
	public sealed class CachedPrivacyPolicy
	{
		/// <summary>WordPress post ID of the policy.</summary>
		[JsonPropertyName("id")]
		public int Id { get; set; }

		/// <summary>The post title, e.g. "Privacy Policy".</summary>
		[JsonPropertyName("title")]
		public string Title { get; set; } = string.Empty;

		/// <summary>
		/// The version stamp recorded against each acceptance.
		/// </summary>
		[JsonPropertyName("version")]
		public string Version { get; set; } = string.Empty;

		/// <summary>
		/// The full policy body as rendered HTML, copied verbatim from
		/// <see cref="PrivacyPolicy.Policy"/>. This is both what the
		/// consent popup displays and what the compliance acceptance
		/// email quotes against the cached version stamp.
		/// </summary>
		[JsonPropertyName("policy")]
		public string Policy { get; set; } = string.Empty;

		/// <summary>
		/// ISO-8601 timestamp of the policy's last modification on the
		/// server, in UTC. Copied verbatim from Scrutiny — useful for
		/// cache-invalidation comparisons and for showing the operator
		/// when the policy they're working with was last updated upstream.
		/// </summary>
		[JsonPropertyName("modified")]
		public string Modified { get; set; } = string.Empty;

		/// <summary>
		/// Local UTC timestamp of when this cache entry was written.
		/// Set by <see cref="Services.Interfaces.IPrivacyPolicyCache.Save"/>;
		/// not populated by Scrutiny. Used by the Settings page to show
		/// "last refreshed N minutes ago" while offline.
		/// </summary>
		[JsonPropertyName("cached_at")]
		public DateTime CachedAt { get; set; }
	}
}
