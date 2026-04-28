using System.Security.Cryptography;
using System.Text;

namespace TheBleedingDeacons.Intergroup.Register.Utilities;

/// <summary>
/// Loads the GDPR / privacy policy text from <c>Resources/Raw/Compliance.txt</c>
/// (a MauiAsset, packaged with the app) and splits it into the parts the
/// rest of the codebase needs:
///
///   • Title    — the first line, with the markdown emphasis markers (<c>*…*</c>) stripped.
///   • Body     — everything after the title, with leading/trailing whitespace trimmed.
///   • Version  — a deterministic short hash of the body, suitable for the
///                <see cref="Services.Interfaces.IComplianceRegistration.RecordAcceptance"/>
///                <c>version</c> argument. Changes automatically when the
///                policy text changes, so we never record acceptance of a
///                statement and stamp it with a stale version string.
///
/// The asset is read once and cached for the process lifetime — the file is
/// embedded in the app bundle and never changes at runtime, so re-reading
/// it on every registration is wasted I/O.
/// </summary>
public static class ComplianceTextLoader
{
    private const string AssetFileName = "Compliance.txt";

    /// <summary>
    /// Cached load. <see cref="Lazy{T}"/> with the publication-only mode
    /// guarantees first-call-wins thread safety without serialising every
    /// subsequent reader on a lock.
    /// </summary>
    private static readonly Lazy<Task<ComplianceText>> _cached = new(LoadInternalAsync);

    /// <summary>
    /// Loads the compliance text. Subsequent calls return the cached result.
    /// </summary>
    public static Task<ComplianceText> LoadAsync() => _cached.Value;

    private static async Task<ComplianceText> LoadInternalAsync()
    {
        using var stream = await FileSystem.OpenAppPackageFileAsync(AssetFileName);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var raw = await reader.ReadToEndAsync();

        // Normalise line endings so the title-line parse is platform-independent.
        var normalised = raw.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

        // Title = first non-empty line, with leading/trailing markdown
        // emphasis markers stripped. The shipped file starts with
        // "*Data Privacy & Consent*" — keep the wording, drop the markers.
        var firstNewline = normalised.IndexOf('\n');
        string titleLine, body;
        if (firstNewline < 0)
        {
            titleLine = normalised;
            body = string.Empty;
        }
        else
        {
            titleLine = normalised[..firstNewline];
            body = normalised[(firstNewline + 1)..].Trim();
        }

        var title = StripEmphasis(titleLine);

        // Short content hash. SHA-256 → first 8 hex chars is plenty for a
        // policy version stamp (collisions only matter for distinguishing
        // *successive* versions of the same file, not arbitrary inputs)
        // and stays well under the server's 50-char cap.
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(body));
        var version = "v" + Convert.ToHexString(hashBytes)[..8].ToLowerInvariant();

        return new ComplianceText(title, body, version);
    }

    /// <summary>
    /// Strips a single matched pair of leading/trailing <c>*</c> or <c>**</c>
    /// markers used as markdown emphasis in the title line. Falls back to
    /// returning the input unchanged if the markers don't match.
    /// </summary>
    private static string StripEmphasis(string line)
    {
        var trimmed = line.Trim();

        if (trimmed.StartsWith("**") && trimmed.EndsWith("**") && trimmed.Length >= 4)
            return trimmed[2..^2].Trim();

        if (trimmed.StartsWith("*") && trimmed.EndsWith("*") && trimmed.Length >= 2)
            return trimmed[1..^1].Trim();

        return trimmed;
    }
}

/// <summary>
/// Parsed compliance text record. Body and Version always travel together
/// — the version is a hash of the body, so a body update implies a version
/// update without any manual bookkeeping.
/// </summary>
/// <param name="Title">Display title for the popup (no emphasis markers).</param>
/// <param name="Body">Full policy body shown to the user and recorded as the acceptance statement.</param>
/// <param name="Version">Stable short hash identifying this exact body text.</param>
public sealed record ComplianceText(string Title, string Body, string Version);
