namespace TheBleedingDeacons.Intergroup.Register.Support;

/// <summary>
/// Generates unique negative integer IDs for locally-created members that have
/// not yet been persisted to the Unity REST API.
///
/// <para><b>Why negative?</b></para>
/// <list type="bullet">
///   <item>Unity (WordPress) assigns positive post IDs — a negative value can
///         never collide with a real Unity member ID.</item>
///   <item>Multiple Register apps running simultaneously each get IDs from a
///         different range, so they won't collide with each other either.</item>
///   <item>Any code that needs to know whether a member requires a
///         <c>CreateMember</c> API call can simply check <c>member.Id &lt; 0</c>.</item>
/// </list>
///
/// <para><b>How it works:</b></para>
/// A one-time device seed is derived from a GUID stored in <see cref="Preferences"/>
/// (persists across app restarts but is unique per device/install). The seed
/// occupies the upper bits of the negative range; a monotonically increasing
/// counter fills the lower bits. The counter is persisted to
/// <see cref="Preferences"/> so that IDs are never reused across app restarts,
/// even when temporary members from a previous session still exist in the
/// local database. This gives each device ~65 535 locally-created members
/// before any theoretical overlap — far more than a single intergroup meeting
/// will ever need.
/// </summary>
public static class TemporaryIdGenerator
{
    private const string DeviceSeedKey = "temp_id_device_seed";
    private const string CounterKey = "temp_id_counter";

    /// <summary>
    /// Upper 16 bits: device seed (1–65 535).
    /// Lower 16 bits: per-session counter (1–65 535).
    /// Combined and negated → always negative, always unique across devices.
    /// </summary>
    private static readonly int DeviceSeed;
    private static int _counter;

    static TemporaryIdGenerator()
    {
        DeviceSeed = GetOrCreateDeviceSeed();

        // Resume the counter from its last persisted value so we never
        // reissue an ID that may still exist in the local SQLite database
        // from a previous app session.
        _counter = Preferences.Default.Get(CounterKey, 0);
    }

    /// <summary>
    /// Returns the next unique negative temporary ID for a locally-created member.
    /// Thread-safe.
    /// </summary>
    public static int Next()
    {
        var count = Interlocked.Increment(ref _counter);

        // Persist so the next app launch continues from here.
        Preferences.Default.Set(CounterKey, count);

        // Combine device seed (upper 16 bits) with counter (lower 16 bits)
        // and negate to guarantee a negative value.
        var combined = (DeviceSeed << 16) | (count & 0xFFFF);
        return -Math.Abs(combined);
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="id"/> is a temporary (negative)
    /// ID that was generated locally and has not yet been resolved by the Unity API.
    /// </summary>
    public static bool IsTemporary(int id) => id < 0;

    /// <summary>
    /// Resets the persisted counter to zero. Call this after a full sync has
    /// deleted all temporary members from the local database, so the ID space
    /// is reclaimed cleanly.
    /// </summary>
    public static void ResetCounter()
    {
        Interlocked.Exchange(ref _counter, 0);
        Preferences.Default.Set(CounterKey, 0);
    }

    private static int GetOrCreateDeviceSeed()
    {
        var stored = Preferences.Default.Get(DeviceSeedKey, 0);
        if (stored is > 0 and <= 0xFFFF)
            return stored;

        // Derive a 16-bit seed from a fresh GUID.
        var bytes = Guid.NewGuid().ToByteArray();
        var seed = (Math.Abs(BitConverter.ToInt32(bytes, 0)) % 0xFFFE) + 1; // 1–65 534

        Preferences.Default.Set(DeviceSeedKey, seed);
        return seed;
    }
}