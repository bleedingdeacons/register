namespace TheBleedingDeacons.Intergroup.Register.Support;

/// <summary>
/// Keeps <see cref="TemporaryIdGenerator"/>'s counter in Preferences.
///
/// <para>The key is the one the generator used when it wrote to Preferences
/// itself, so a tablet upgraded mid-meeting resumes its count rather than
/// reissuing an ID an orphaned row may still hold.</para>
/// </summary>
public sealed class PreferencesTemporaryIdStore : ITemporaryIdStore
{
	private const string CounterKey = "temp_id_counter";

	/// <inheritdoc />
	public int Get() => Preferences.Default.Get(CounterKey, 0);

	/// <inheritdoc />
	public void Set(int value) => Preferences.Default.Set(CounterKey, value);
}
