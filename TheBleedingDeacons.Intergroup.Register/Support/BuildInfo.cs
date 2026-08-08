using System.Runtime.InteropServices;

namespace TheBleedingDeacons.Intergroup.Register.Support;

/// <summary>
/// Identifies the running build: the app version the operator sees, the
/// build number behind it, and the .NET runtime it is executing on.
///
/// The runtime part is the reason this type exists. While the .NET 9 and
/// .NET 10 branches are both live, an APK on a tablet is otherwise
/// indistinguishable — same icon, same version number, same UI — and
/// "which one is installed here?" is the first question asked whenever a
/// device misbehaves. <see cref="Framework"/> answers it from the device
/// itself, with no cable and no build log.
///
/// Everything is read at runtime rather than baked in at compile time, so
/// there is no build plumbing to keep in step and no way for the reported
/// value to drift from the assembly actually running.
/// </summary>
public static class BuildInfo
{
    /// <summary>
    /// User-facing version, from <c>$(ApplicationDisplayVersion)</c> — e.g. "1.0.26".
    /// </summary>
    public static string Version => AppInfo.VersionString;

    /// <summary>
    /// Build number, from <c>$(ApplicationVersion)</c> — e.g. "1". Distinct from
    /// <see cref="Version"/>: the stores order builds by this, not by the display string.
    /// </summary>
    public static string Build => AppInfo.BuildString;

    /// <summary>
    /// The .NET runtime executing this process — e.g. ".NET 9.0.18" or ".NET 10.0.10".
    /// Reported by the runtime itself, so it reflects what is actually loaded rather
    /// than what the project file asked for.
    /// </summary>
    public static string Framework => RuntimeInformation.FrameworkDescription;

    /// <summary>
    /// The three joined for display and logging — e.g. "1.0.26 (build 1) — .NET 10.0.10".
    /// Used verbatim by the Settings page and the startup log banner so both always agree.
    /// </summary>
    public static string Summary => $"{Version} (build {Build}) — {Framework}";
}
