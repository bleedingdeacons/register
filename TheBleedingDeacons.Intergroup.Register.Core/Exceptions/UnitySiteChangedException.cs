namespace TheBleedingDeacons.Intergroup.Register.Exceptions;

/// <summary>
/// Thrown, before anything is sent or replaced, when Freedom points the
/// tablet at a different Unity site from the one its records came from.
///
/// <para>Like <see cref="NoActivePrivacyPolicyException"/>, the message is
/// the operator's: the Admin page shows it verbatim under "Sync failed" or
/// "Reconciliation failed", so it says what happened and both ways out.</para>
/// </summary>
public sealed class UnitySiteChangedException : InvalidOperationException
{
	public UnitySiteChangedException(string recordsFrom, string configuredSite, string message)
		: base(message)
	{
		RecordsFrom = recordsFrom;
		ConfiguredSite = configuredSite;
	}

	/// <summary>The site the tablet's records came from.</summary>
	public string RecordsFrom { get; }

	/// <summary>The site Freedom points the tablet at now.</summary>
	public string ConfiguredSite { get; }

	/// <summary>Finishing the meeting: the records would go to the wrong site.</summary>
	public static UnitySiteChangedException Sending(string recordsFrom, string configuredSite) =>
		new(recordsFrom, configuredSite,
			$"Nothing was sent. This tablet's records came from {recordsFrom}, but Freedom now points it at " +
			$"{configuredSite}, where the same ids belong to other groups and members. Point Freedom back at " +
			$"{recordsFrom} to send them, or Reset Device to discard them.");

	/// <summary>Loading Unity: the unsent records would be replayed over the new site's.</summary>
	public static UnitySiteChangedException Replacing(string recordsFrom, string configuredSite) =>
		new(recordsFrom, configuredSite,
			$"Nothing was loaded. This tablet has unsent registrations from {recordsFrom}, but Freedom now points it at " +
			$"{configuredSite}. Point Freedom back at {recordsFrom} and finish that meeting, or Reset Device to discard them.");
}
