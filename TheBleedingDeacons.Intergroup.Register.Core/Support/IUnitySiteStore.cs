namespace TheBleedingDeacons.Intergroup.Register.Support;

/// <summary>
/// Where <see cref="Services.UnitySiteGuard"/> keeps the Unity site the
/// tablet's records came from. Preferences in the app; whatever a test
/// likes elsewhere.
/// </summary>
public interface IUnitySiteStore
{
	/// <summary>The site the records came from, or null when nobody knows.</summary>
	string? Get();

	/// <summary>Stores <paramref name="site"/>; null forgets it.</summary>
	void Set(string? site);
}
