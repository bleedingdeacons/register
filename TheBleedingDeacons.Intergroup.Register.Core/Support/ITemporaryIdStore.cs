namespace TheBleedingDeacons.Intergroup.Register.Support;

/// <summary>
/// Where <see cref="TemporaryIdGenerator"/> keeps its counter between launches.
/// Preferences in the app; whatever a test likes elsewhere.
/// </summary>
public interface ITemporaryIdStore
{
	/// <summary>The stored counter, or zero when nothing has been stored.</summary>
	int Get();

	/// <summary>Stores <paramref name="value"/>.</summary>
	void Set(int value);
}
