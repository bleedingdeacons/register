namespace TheBleedingDeacons.Intergroup.Register.Support;

/// <summary>
/// Generates negative integer IDs for locally-created members that have not
/// yet been persisted to the Unity REST API.
///
/// <para><b>Why negative?</b></para>
/// Unity (WordPress) assigns positive post IDs, so a negative value can never
/// collide with a real Unity member ID in the local SQLite database. Any code
/// that needs to know whether a member requires a <c>CreateMember</c> API call
/// can simply check <c>member.Id &lt; 0</c> (see <see cref="IsTemporary"/>).
///
/// <para><b>Scope of uniqueness:</b></para>
/// Temp IDs are a <i>local</i> concern only — they live in the device's SQLite
/// database from creation until reconciliation, and are never transmitted to
/// Unity. The reconcile flow sends member data via <c>CreateMemberAsync</c> and
/// Unity returns a fresh positive ID, which replaces the temp ID on the next
/// sync. So the counter only needs to be unique within a single device over the
/// lifetime of any row that still references it. Multiple Register apps running
/// the same meeting in parallel can each use their own counter independently —
/// their negative IDs never meet.
///
/// <para><b>Persistence:</b></para>
/// The counter is persisted to the <see cref="ITemporaryIdStore"/> given to
/// <see cref="Use"/> on every increment — Preferences, in the app.
/// This is intentional: if the app crashes after generating a temp ID but
/// before the member row is saved to SQLite, the next launch must not reissue
/// the same ID — an orphan row from the crashed session may still be in the
/// database and would cause a primary-key collision. Synchronous persistence
/// is cheap at the rate these are generated (handful per meeting).
///
/// <para><b>The store is handed in</b> because this class lives in
/// Register.Core, which cannot see Preferences. Until <see cref="Use"/> is
/// called the counter lives in memory only, starting from zero — which is
/// exactly the crash-and-reissue hazard above, so the app calls it at
/// startup before anything can create a member.</para>
/// </summary>
public static class TemporaryIdGenerator
{
	private static ITemporaryIdStore? _store;

	// Counter decreases monotonically: first call returns -1, next -2, etc.
	// Initial value loaded from the store so we resume where we left off.
	private static int _counter;

	/// <summary>
	/// Persists the counter to <paramref name="store"/> from now on, resuming
	/// from whatever it last held.
	/// </summary>
	public static void Use(ITemporaryIdStore store)
	{
		ArgumentNullException.ThrowIfNull(store);

		_store = store;
		Interlocked.Exchange(ref _counter, LoadCounter(store));
	}

	/// <summary>
	/// Returns the next unique negative temporary ID for this device.
	/// Thread-safe.
	/// </summary>
	public static int Next()
	{
		var next = Interlocked.Decrement(ref _counter);

		// Persist so the next app launch continues from here and cannot
		// reissue an ID still referenced by an orphaned row.
		try
		{
			_store?.Set(next);
		}
		catch
		{
			// Store write failed (rare — disk full, etc). Counter
			// continues in memory; worst case a crash + relaunch reissues
			// an ID that collides with an orphan, which surfaces as a
			// SQLite PK violation the caller can handle. Silent retry on
			// next call is fine.
		}

		return next;
	}

	/// <summary>
	/// Returns <c>true</c> when <paramref name="id"/> is a temporary (negative)
	/// ID that was generated locally and has not yet been resolved by the Unity API.
	/// </summary>
	public static bool IsTemporary(int id) => id < 0;

	/// <summary>
	/// Resets the persisted counter to zero. Safe to call only when no
	/// temporary members remain in the local database (e.g. immediately
	/// after a full purge, or after reconciliation + sync has replaced
	/// every temp ID with its real Unity counterpart).
	/// </summary>
	public static void ResetCounter()
	{
		Interlocked.Exchange(ref _counter, 0);
		try
		{
			_store?.Set(0);
		}
		catch
		{
			// See Next() — non-fatal.
		}
	}

	private static int LoadCounter(ITemporaryIdStore store)
	{
		try
		{
			var stored = store.Get();
			// Guard against positive values ending up in the pref (e.g. from
			// an older version or corrupt prefs). Counter must start at 0 or
			// below — Next() always decrements, so a positive seed would hand
			// out positive IDs and silently collide with Unity's post IDs.
			return stored > 0 ? 0 : stored;
		}
		catch
		{
			return 0;
		}
	}
}