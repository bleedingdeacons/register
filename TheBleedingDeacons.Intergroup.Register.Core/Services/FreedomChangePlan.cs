namespace TheBleedingDeacons.Intergroup.Register.Services;

/// <summary>
/// What has to happen on the tablet when a Freedom key changes, beyond the
/// configuration cache being cleared — which always happens.
/// </summary>
[Flags]
public enum FreedomEffects
{
	/// <summary>Nothing holds the value: it is read afresh every time it is used.</summary>
	None = 0,

	/// <summary>The email service holds the SMTP settings and has to be given the new ones.</summary>
	Smtp = 1,

	/// <summary>The Better Stack sink holds its endpoint and token and has to be rebuilt.</summary>
	LogShipping = 2,

	/// <summary>
	/// The tablet may now point at a different Unity site from the one its
	/// records came from. See <see cref="UnitySiteGuard"/>.
	/// </summary>
	UnitySite = 4,
}

/// <summary>
/// Works out, from the keys a Freedom sync changed, which parts of the
/// running tablet still hold the old values.
///
/// <para><b>Every key Register reads is in <see cref="KeyEffects"/>, with what
/// a change to it means.</b> A test holds every constant in
/// <see cref="FreedomSettings"/> to having an entry, so a new key cannot be
/// added without somebody deciding what a change to it has to do. Until
/// 2026-10-23 this was two prefixes in the app's startup — <c>smtp.</c> and
/// <c>betterstack.</c> — which no test could reach, and which a key in a
/// new group would have slipped past: stored, and quietly not used until
/// the next start.</para>
///
/// <para>Exact keys, not prefixes. A key the site holds and this build does
/// not read is <see cref="Unused"/>, and is logged as such — usually the
/// site has been set up for a newer build, or an old key was never
/// removed.</para>
///
/// <para>Removed keys count exactly as updated ones do: a value that has
/// gone is a value that changed.</para>
/// </summary>
public sealed record FreedomChangePlan(FreedomEffects Effects, IReadOnlyList<string> Unused)
{
	/// <summary>
	/// Each key Register reads, and what a change to it means. The ones
	/// that mean nothing are read afresh on every use: the Unity client
	/// factory reads the API key for every sync, and the compliance
	/// contact is read for every acceptance email.
	/// </summary>
	public static IReadOnlyDictionary<string, FreedomEffects> KeyEffects { get; } =
		new Dictionary<string, FreedomEffects>(StringComparer.Ordinal)
		{
			[FreedomSettings.SmtpHost] = FreedomEffects.Smtp,
			[FreedomSettings.SmtpPort] = FreedomEffects.Smtp,
			[FreedomSettings.SmtpUsername] = FreedomEffects.Smtp,
			[FreedomSettings.SmtpPassword] = FreedomEffects.Smtp,
			[FreedomSettings.SmtpEnableSsl] = FreedomEffects.Smtp,
			[FreedomSettings.SmtpFromDisplayName] = FreedomEffects.Smtp,
			[FreedomSettings.SmtpTimeoutSeconds] = FreedomEffects.Smtp,
			[FreedomSettings.UnityBaseUrl] = FreedomEffects.UnitySite,
			[FreedomSettings.UnityApiKey] = FreedomEffects.None,
			[FreedomSettings.BetterStackEndpoint] = FreedomEffects.LogShipping,
			[FreedomSettings.BetterStackSourceToken] = FreedomEffects.LogShipping,
			[FreedomSettings.ComplianceEmail] = FreedomEffects.None,
		};

	/// <summary>The plan for a sync that fetched <paramref name="updated"/> and removed <paramref name="removed"/>.</summary>
	public static FreedomChangePlan For(IEnumerable<string> updated, IEnumerable<string> removed)
	{
		ArgumentNullException.ThrowIfNull(updated);
		ArgumentNullException.ThrowIfNull(removed);

		var effects = FreedomEffects.None;
		var unused = new List<string>();

		foreach (var key in updated.Concat(removed).Distinct(StringComparer.Ordinal))
		{
			if (KeyEffects.TryGetValue(key, out var effect))
				effects |= effect;
			else
				unused.Add(key);
		}

		return new FreedomChangePlan(effects, unused);
	}

	/// <summary>Whether the plan includes <paramref name="effect"/>.</summary>
	public bool Has(FreedomEffects effect) => (Effects & effect) == effect && effect != FreedomEffects.None;
}
