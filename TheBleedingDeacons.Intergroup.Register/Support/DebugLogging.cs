namespace TheBleedingDeacons.Intergroup.Register.Support;

/// <summary>
/// This tablet's Debug Logging switch, kept in Preferences. On by default,
/// and on when Preferences cannot be read or hold something unreadable: the
/// lines are worth having when something goes wrong, and an operator can
/// turn them off.
///
/// <para>Static because the logger is built before the container exists;
/// <see cref="Services.ConfigurationService"/> reads and sets it from there
/// on.</para>
/// </summary>
public static class DebugLogging
{
	private const string PreferenceKey = "debug_logging_enabled";

	/// <summary>The level the logger is built with; see <see cref="DebugLoggingLevel"/>.</summary>
	public static DebugLoggingLevel Level { get; } = new(Read());

	public static bool IsEnabled => Level.IsEnabled;

	public static void Set(bool enabled)
	{
		Level.Set(enabled);

		try
		{
			Preferences.Default.Set(PreferenceKey, enabled ? "true" : "false");
		}
		catch (Exception)
		{
			// In force until the next start all the same, which is back to on.
		}
	}

	private static bool Read()
	{
		try
		{
			var raw = Preferences.Default.Get(PreferenceKey, string.Empty);
			return string.IsNullOrEmpty(raw) || !bool.TryParse(raw, out var value) || value;
		}
		catch (Exception)
		{
			return true;
		}
	}
}
