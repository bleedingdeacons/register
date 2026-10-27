namespace TheBleedingDeacons.Intergroup.Register.Support;

/// <summary>
/// This tablet's key for <see cref="EmailRedactor"/>'s tokens, made the first
/// time it is asked for and kept in Preferences.
///
/// <para>It never leaves the tablet: that is what keeps a token from being
/// matched to an address by anyone holding only the logs, and what keeps one
/// tablet's tokens from lining up with another's. Preferences are the app's
/// private storage and are excluded from backup (<c>allowBackup="false"</c>).
/// Losing it costs nothing but continuity: a new key, new tokens.</para>
/// </summary>
public static class LogRedactionKey
{
	private const string PreferenceKey = "log_redaction_key";

	private static readonly Lock Sync = new();
	private static byte[]? _key;

	public static byte[] Get()
	{
		lock (Sync)
		{
			return _key ??= LoadOrCreate();
		}
	}

	private static byte[] LoadOrCreate()
	{
		try
		{
			var stored = Preferences.Default.Get(PreferenceKey, string.Empty);
			if (stored.Length > 0)
			{
				var key = Convert.FromBase64String(stored);
				if (key.Length >= 16)
					return key;
			}
		}
		catch (FormatException)
		{
			// Unreadable: replaced below, as if there had been none.
		}

		var fresh = EmailRedactor.NewKey();
		try
		{
			Preferences.Default.Set(PreferenceKey, Convert.ToBase64String(fresh));
		}
		catch (Exception)
		{
			// Still redacts with it. Tokens just will not carry over to the
			// next start, which is a loss of continuity, not of privacy.
		}

		return fresh;
	}
}
