namespace TheBleedingDeacons.Intergroup.Register.Support;

/// <summary>
/// Keeps the Unity site the tablet's records came from in Preferences, for
/// <see cref="Services.UnitySiteGuard"/>. Not a secret: it is the address
/// in <c>unity.base_url</c>, which the API Settings page shows anyway.
/// </summary>
public sealed class PreferencesUnitySiteStore : IUnitySiteStore
{
	private const string SiteKey = "unity_records_site";

	/// <inheritdoc />
	public string? Get()
	{
		var site = Preferences.Default.Get(SiteKey, string.Empty);
		return string.IsNullOrWhiteSpace(site) ? null : site;
	}

	/// <inheritdoc />
	public void Set(string? site)
	{
		if (string.IsNullOrWhiteSpace(site))
			Preferences.Default.Remove(SiteKey);
		else
			Preferences.Default.Set(SiteKey, site);
	}
}
