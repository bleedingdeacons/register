using Serilog;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Services;

/// <summary>
/// Where the two append-only logs — <see cref="RegistrationEventLog"/> and
/// <see cref="ComplianceEventLog"/> — live on this device.
///
/// <para>This decision used to be made inside each log, and it was the one
/// thing in either that needed MAUI. It moved out when the logs moved to
/// Register.Core, so a test can hand them a temporary file instead. Both
/// logs share this one ladder, so they always end up in the same
/// directory.</para>
/// </summary>
public static class EventLogDirectory
{
	private static readonly ILogger Logger = AppLogger.ForContext(nameof(EventLogDirectory));

	/// <summary>
	/// Resolves the user's Documents folder, creating it if it does not yet
	/// exist. Placing the logs in Documents (rather than
	/// <see cref="FileSystem.AppDataDirectory"/>) makes them visible to the
	/// user for inspection and to IT support for collection, and keeps them
	/// outside the app's sandbox-scoped data directory so uninstalling the
	/// app does not take the crash log with it.
	/// </summary>
	public static string Resolve()
	{
		// Environment.SpecialFolder.MyDocuments resolves to:
		//   • Windows  → %USERPROFILE%\Documents
		//   • macOS    → ~/Documents
		//   • iOS      → the app's Documents directory (sandbox; still the
		//                right place — it's user-visible via the Files app)
		//   • Android  → the app's private files dir; the public Documents
		//                folder is not directly available through
		//                Environment.SpecialFolder, so we fall back below.
		var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

		if (string.IsNullOrEmpty(documents))
		{
			// Last-resort fallback for platforms where MyDocuments isn't
			// mapped. Keeps the app functional rather than crashing at
			// first registration.
			documents = FileSystem.AppDataDirectory;
		}

		try
		{
			Directory.CreateDirectory(documents);
		}
		catch (Exception ex)
		{
			// If we can't create or access the Documents folder for any
			// reason (permissions, read-only volume), fall back to the
			// app data directory rather than fail hard — the logs are a
			// durability aid, not a feature the app can't start without.
			Logger.Warning(ex, "Could not prepare Documents folder {Path}; falling back to AppDataDirectory", documents);
			documents = FileSystem.AppDataDirectory;
		}

		return documents;
	}
}
