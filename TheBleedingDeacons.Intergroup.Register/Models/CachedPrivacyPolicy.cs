using System.Text.Json.Serialization;

namespace TheBleedingDeacons.Intergroup.Register.Models
{
	/// <summary>
	/// A privacy policy as cached on-device, populated by the sync stage
	/// from the Scrutiny <c>/privacy-policies/active</c> endpoint and read
	/// from cache by the registration flow.
	///
	/// <para>This is deliberately a slimmer shape than
	/// <see cref="PrivacyPolicy"/>: we drop the <c>Policy</c> HTML body,
	/// because the body shown to the user during acceptance comes from
	/// <c>Resources/Raw/Terms.txt</c>, not from Scrutiny. The cache
	/// only carries the audit-trail fields (Id, Version, Contact,
	/// Modified) that we need to record against each acceptance.</para>
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
		/// The version stamp recorded against each acceptance. This is
		/// the value that wins over the version parsed out of
		/// <c>Terms.txt</c> when the two disagree.
		/// </summary>
		[JsonPropertyName("version")]
		public string Version { get; set; } = string.Empty;

		/// <summary>Named data-protection contact.</summary>
		[JsonPropertyName("contact")]
		public string Contact { get; set; } = string.Empty;

		/// <summary>Email address for data-protection enquiries.</summary>
		[JsonPropertyName("contact_email")]
		public string ContactEmail { get; set; } = string.Empty;

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
