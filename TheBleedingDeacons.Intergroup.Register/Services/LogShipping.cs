using TheBleedingDeacons.Inventory;

namespace TheBleedingDeacons.Intergroup.Register.Services;

/// <summary>
/// Register's one rule on top of Inventory's log shipper.
/// </summary>
/// <remarks>
/// <para>Inventory tells three answers apart: no configuration (not told yet,
/// so hold), a valid one (ship) and an invalid one (told not to ship, so drop
/// what is held). Register's settings come from the tablet or from Freedom,
/// and nothing in either ever says "do not ship". A missing endpoint or token
/// only ever means not set up yet — the tablet has not signed in to Freedom, or
/// nobody has typed the token in — so Register holds, and what was held ships
/// the moment it is set up.</para>
///
/// <para>Before Inventory the same state logged to the file only, so the first
/// minutes on a new tablet never reached Better Stack at all.</para>
/// </remarks>
public static class LogShipping
{
	public static void Ship(this ILogShipper shipper, BetterStackConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(shipper);
		ArgumentNullException.ThrowIfNull(configuration);

		shipper.Reconfigure(configuration.IsValid() ? configuration : null);
	}
}
