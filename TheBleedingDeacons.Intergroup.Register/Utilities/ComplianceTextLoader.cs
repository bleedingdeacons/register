\xEF\xBB\xBFusing System.Security.Cryptography;
using System.Text;

namespace TheBleedingDeacons.Intergroup.Register.Utilities;

/// <summary>
/// Loads the GDPR / privacy policy text from <c>Resources/Raw/Compliance.txt</c>
/// (a MauiAsset, packaged with the app) and splits it into the parts the
/// rest of the codebase needs:
///
///   • Title    — the first line, with the markdown emphasis markers (<c>*…*</c>) stripped.
///   • Body     — everything between the title and the version stamp,
///                with leading/trailing whitespace trimmed. The version
///                line itself is removed from the body so it isn't shown
///                to the user as part of the policy prose.
///   • Version  — the last non-empty line, when it looks like a version
///                stamp (e.g. <c>v1.0.0 - 2026-04-28</c>). Used as the
///                <c>version</c> argument to
///                <see cref="Services.Interfaces.IComplianceRegistration.RecordAcceptance"/>.
///                When the file has no recognisable version stamp we fall
///                back to a deterministic short hash of the body, so old
///                or test files still produce a stable stamp rather than
///                blowing up the loader.
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

        // Normalise line endings so the title/version parses are platform-independent.
        var normalised = raw.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

        // Title = first non-empty line, with leading/trailing markdown
        // emphasis markers stripped. The shipped file starts with
        // "*Data Privacy & Consent*" — keep the wording, drop the markers.
        var firstNewline = normalised.IndexOf('\n');
        string titleLine, afterTitle;
        if (firstNewline < 0)
        {
            titleLine = normalised;
            afterTitle = string.Empty;
        }
        else
        {
            titleLine = normalised[..firstNewline];
            afterTitle = normalised[(firstNewline + 1)..];
        }

        var title = StripEmphasis(titleLine);

        // Version = last non-empty line if it looks like a version stamp.
        // We walk lines from the end so trailing blank lines or accidental
        // whitespace at the foot of the file don't throw the parse off.
        // Splitting by '\n' (already normalised above) gives us indexable lines.
        var lines = afterTitle.Split('\n');
        int versionLineIndex = -1;
        string? version = null;

        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var candidate = lines[i].Trim();
            if (candidate.Length == 0) continue;

            if (LooksLikeVersionStamp(candidate))
            {
                version = candidate;
                versionLineIndex = i;
            }

            // First non-empty line from the end decides the outcome —
            // either it's the version, or there isn't one. Don't keep
            // searching upward; an earlier "v…" inside the policy prose
            // shouldn't be picked up.
            break;
        }

        // Body is everything after the title, with the version line (and
        // any trailing blanks below it) removed. When there's no version
        // line, the body is simply afterTitle.
        string body;
        if (versionLineIndex >= 0)
        {
            body = string.Join('\n', lines, 0, versionLineIndex).Trim();
        }
        else
        {
            body = afterTitle.Trim();
        }

        // Fallback: if the file didn't carry an explicit version stamp,
        // derive a deterministic short hash of the body. Keeps older or
        // test fixtures working and ensures every accepted statement is
        // still paired with *something* identifying for the audit trail.
        if (version is null)
        {
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(body));
            version = "v" + Convert.ToHexString(hashBytes)[..8].ToLowerInvariant();
        }

        return new ComplianceText(title, body, version);
    }

    /// <summary>
    /// Heuristic for "this line is a version stamp", not policy prose.
    /// Accepts strings that start with <c>v</c> or <c>V</c> followed
    /// immediately by a digit — covers <c>v1</c>, <c>v1.0.0</c>,
    /// <c>v1.0.0 - 2026-04-28</c>, and similar. Anything else (including
    /// a stray sentence that happens to begin with "v") is rejected.
    /// </summary>
    private static bool LooksLikeVersionStamp(string line)
    {
        if (line.Length < 2) return false;
        if (line[0] != 'v' && line[0] != 'V') return false;
        return char.IsDigit(line[1]);
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
/// Parsed compliance text record. The version is read from the policy
/// file's trailing <c>v…</c> line when present, otherwise derived from a
/// short hash of the body — either way the body and version are kept in
/// sync, so an accepted statement is always paired with an identifier
/// that points back at exactly the text the user saw.
/// </summary>
/// <param name="Title">Display title for the popup (no emphasis markers).</param>
/// <param name="Body">Full policy body shown to the user and recorded as the acceptance statement. Excludes the version line.</param>
/// <param name="Version">Version stamp identifying this exact body text (e.g. <c>v1.0.0 - 2026-04-28</c>).</param>
public sealed record ComplianceText(string Title, string Body, string Version);
