using System.Text;

namespace TheBleedingDeacons.Intergroup.Register.Utilities;

/// <summary>
/// Loads the bundled privacy-policy prose from
/// <c>Resources/Raw/Terms.txt</c> (a MauiAsset packaged with the app)
/// and returns it verbatim as a single string for display in the
/// AcceptTerms popup.
///
/// <para>This loader has one job: produce the body text shown to the
/// user during consent capture. It does <b>not</b> parse a title,
/// version, or any other structured field — those all come from the
/// on-device privacy-policy cache populated by the sync stage (see
/// <see cref="Services.Interfaces.IPrivacyPolicyCache"/>). The cached
/// Scrutiny record is the single source of truth for the audit-trail
/// fields; <c>Terms.txt</c> is purely the human-readable display
/// surface.</para>
///
/// <para>The asset is read once and cached for the process lifetime —
/// the file is embedded in the app bundle and never changes at
/// runtime, so re-reading it on every registration would be wasted
/// I/O. <see cref="Lazy{T}"/> with the default publication-only mode
/// guarantees first-call-wins thread safety without serialising every
/// subsequent reader on a lock.</para>
/// </summary>
public static class TermsTextLoader
{
    private const string AssetFileName = "Terms.txt";

    private static readonly Lazy<Task<string>> _cached = new(LoadInternalAsync);

    /// <summary>
    /// Returns the full contents of <c>Terms.txt</c> as a single
    /// string. Subsequent calls return the cached result.
    /// </summary>
    public static Task<string> LoadAsync() => _cached.Value;

    private static async Task<string> LoadInternalAsync()
    {
        using var stream = await FileSystem.OpenAppPackageFileAsync(AssetFileName);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync();
    }
}
