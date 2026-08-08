namespace TheBleedingDeacons.Intergroup.Register.Support;

/// <summary>
/// Issues the negative placeholder IDs used for locally-created members that
/// Unity has not seen yet. See <see cref="TemporaryIdGenerator"/> for why the
/// IDs are negative and why the counter is persisted.
/// </summary>
public interface ITemporaryIdGenerator
{
	/// <summary>
	/// Returns the next unique negative temporary ID for this device.
	/// Thread-safe.
	/// </summary>
	int Next();

	/// <summary>
	/// Resets the persisted counter to zero. Safe to call only when no
	/// temporary members remain in the local database (e.g. immediately
	/// after a full purge, or after reconciliation + sync has replaced
	/// every temp ID with its real Unity counterpart).
	/// </summary>
	void ResetCounter();
}
