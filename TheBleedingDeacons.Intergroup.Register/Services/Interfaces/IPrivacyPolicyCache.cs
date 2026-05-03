using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Services.Interfaces
{
	/// <summary>
	/// On-device cache of the active privacy policy fetched from
	/// Scrutiny during the sync stage. The registration flow reads
	/// from this cache rather than calling Scrutiny live, so consent
	/// can be recorded during a meeting that's gone offline since the
	/// last sync.
	///
	/// <para>Backed by <c>Preferences</c> as a single JSON blob keyed
	/// by <c>scrutiny.cachedPolicy</c>. The implementation is
	/// thread-safe — Preferences is process-wide — and writes are
	/// atomic at the granularity of the whole record, so a crash
	/// mid-write can't leave a half-populated cache.</para>
	///
	/// <para>This is a read-mostly cache. The only writers are the
	/// sync stage and the Settings page's manual Refresh button (which
	/// calls the same path as sync would). Everything else reads.</para>
	/// </summary>
	public interface IPrivacyPolicyCache
	{
		/// <summary>
		/// Returns the most recently cached active policy, or
		/// <c>null</c> if none has ever been cached on this device.
		///
		/// <para>A null return is the "device has never synced
		/// successfully against an active policy" state, not an error
		/// — registration flows treat it as a hard stop because there
		/// is no version to record against an acceptance, but the UI
		/// can render a "needs sync" message instead of a stack trace.</para>
		/// </summary>
		CachedPrivacyPolicy? GetCached();

		/// <summary>
		/// Persists <paramref name="policy"/> as the new cached active
		/// policy, stamping <see cref="CachedPrivacyPolicy.CachedAt"/>
		/// to <see cref="DateTime.UtcNow"/>. Replaces any existing
		/// cache entry; there is only one slot.
		/// </summary>
		/// <param name="policy">
		/// The policy returned by Scrutiny's <c>/privacy-policies/active</c>
		/// endpoint. Must not be null — callers that received a null
		/// from Scrutiny should call <see cref="Clear"/> instead.
		/// </param>
		void Save(PrivacyPolicy policy);

		/// <summary>
		/// Clears the cached policy. Called by the sync stage when
		/// Scrutiny reports no active policy, so a stale cache from
		/// a previous successful sync isn't silently reused after the
		/// upstream policy has been retracted.
		///
		/// <para>Idempotent — calling this when the cache is already
		/// empty is a no-op.</para>
		/// </summary>
		void Clear();
	}
}
