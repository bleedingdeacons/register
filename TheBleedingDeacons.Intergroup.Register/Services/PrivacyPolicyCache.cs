using System.Text.Json;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
	/// <summary>
	/// Default <see cref="IPrivacyPolicyCache"/> implementation, backed
	/// by <c>Microsoft.Maui.Storage.Preferences</c>. See the interface
	/// docs for the rationale behind the cache's role.
	///
	/// <para><b>Storage shape.</b> Single JSON blob under the
	/// <c>scrutiny.cachedPolicy</c> key. JSON rather than separate keys
	/// per field because the writes need to be atomic — a crash between
	/// "version updated" and "id updated" would leave the cache
	/// referring to a different policy than its version stamp suggests,
	/// which would silently corrupt the audit trail.</para>
	///
	/// <para><b>Lifetime.</b> Singleton. The cache is stateless beyond
	/// Preferences itself, which is process-wide and thread-safe.</para>
	///
	/// <para><b>Preferences is injected</b> as <see cref="IPreferences"/>
	/// rather than reached through the <c>Preferences.Default</c> static.
	/// The static throws outside a MAUI host, which made the corrupt-blob
	/// and read-failure recovery paths below impossible to test — and those
	/// paths are the whole reason the class is written the way it is.</para>
	/// </summary>
	public sealed class PrivacyPolicyCache : IPrivacyPolicyCache
	{
		private static readonly ILogger Logger = AppLogger.ForContext<PrivacyPolicyCache>();

		private readonly IPreferences _preferences;

		public PrivacyPolicyCache(IPreferences preferences)
		{
			_preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
		}

		// Namespaced under "scrutiny." so the key doesn't collide with
		// any unrelated Preferences key now or in future. Everything
		// related to Scrutiny lives under this prefix.
		private const string CacheKey = "scrutiny.cachedPolicy";

		// Same options instance reused across calls — the JsonSerializer
		// metadata cache is keyed by the options object, so allocating
		// a fresh one per call would defeat that cache. Mirrors the
		// same pattern ScrutinyClient uses.
		private static readonly JsonSerializerOptions JsonOptions = new()
		{
			PropertyNameCaseInsensitive = true,
		};

		public CachedPrivacyPolicy? GetCached()
		{
			string raw;
			try
			{
				raw = _preferences.Get(CacheKey, string.Empty);
			}
			catch (Exception ex)
			{
				// Preferences read failures are very rare on a healthy
				// device, but on Android they can happen if the user
				// has cleared app data while the app is paused. Treat
				// it as "no cache" rather than letting it bubble up
				// into the registration flow.
				Logger.Warning(ex, "Failed to read cached privacy policy from Preferences");
				return null;
			}

			if (string.IsNullOrWhiteSpace(raw))
				return null;

			try
			{
				return JsonSerializer.Deserialize<CachedPrivacyPolicy>(raw, JsonOptions);
			}
			catch (JsonException ex)
			{
				// A corrupted blob is no better than a missing one —
				// log loudly and pretend it isn't there. The next sync
				// will overwrite it with a known-good shape.
				Logger.Warning(ex, "Cached privacy policy is corrupt; ignoring");
				return null;
			}
		}

		public void Save(PrivacyPolicy policy)
		{
			ArgumentNullException.ThrowIfNull(policy);

			var entry = new CachedPrivacyPolicy
			{
				Id = policy.Id,
				Title = policy.Title,
				Version = policy.Version,
				Policy = policy.Policy,
				Modified = policy.Modified,
				CachedAt = DateTime.UtcNow,
			};

			try
			{
				var json = JsonSerializer.Serialize(entry, JsonOptions);
				_preferences.Set(CacheKey, json);
				Logger.Information(
					"Cached active privacy policy: id={Id} version={Version}",
					entry.Id, entry.Version);
			}
			catch (Exception ex)
			{
				// Rare: a serialisation failure on a record this small
				// would be a programmer error, and a Preferences write
				// failure is a platform problem. Log and continue —
				// the in-memory call has the data, and the next sync
				// will retry the write.
				Logger.Warning(ex, "Failed to cache active privacy policy");
			}
		}

		public void Clear()
		{
			try
			{
				_preferences.Remove(CacheKey);
				Logger.Information("Cleared cached privacy policy");
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Failed to clear cached privacy policy");
			}
		}
	}
}
