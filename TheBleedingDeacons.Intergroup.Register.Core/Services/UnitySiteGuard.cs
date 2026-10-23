using Serilog;
using TheBleedingDeacons.Intergroup.Register.Exceptions;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Services;

/// <summary>
/// Where the tablet's records came from, against where Freedom points it now.
/// </summary>
/// <param name="RecordsFrom">The site the last Load Unity fetched from; null when nobody knows.</param>
/// <param name="ConfiguredSite">The site <c>unity.base_url</c> names now; empty when Freedom holds none.</param>
/// <param name="Changed">Both are known and they are different sites.</param>
/// <param name="HasUnsentWork">Either log has entries, or a meeting is in progress.</param>
public sealed record UnitySiteCheck(string? RecordsFrom, string ConfiguredSite, bool Changed, bool HasUnsentWork);

/// <summary>
/// Keeps one site's records from being sent to another.
///
/// <para><b>Ids are only meaningful on the site that issued them.</b>
/// Group 12 on <c>/amber</c> is not group 12 on the live site, and meeting
/// 77 may not exist there at all. Before this, nothing recorded which site
/// the replica came from, and the Unity client factory reads
/// <c>unity.base_url</c> afresh on every call: so a Freedom change to it
/// mid-meeting meant Finish Meeting sent the old site's registrations,
/// member edits and consent to the new one, and a Load Unity replayed the
/// old site's unsent logs over the new site's records.</para>
///
/// <para>So Load Unity records the site it fetched from, and:</para>
/// <list type="bullet">
/// <item><b>Finishing</b> is refused when Freedom points elsewhere — always,
/// since everything a finish sends came from the recorded site.</item>
/// <item><b>Loading</b> is refused only while there is unsent work: a
/// replica with nothing pending is simply replaced, which is what loading
/// does anyway.</item>
/// </list>
///
/// <para><b>Nothing is thrown away.</b> Pointing Freedom back at the old
/// site lets the meeting finish as if nothing happened; Reset Device
/// discards the records and forgets the site. Which of those is right is
/// the operator's call, not the tablet's.</para>
///
/// <para>A tablet that has not loaded since this shipped has no recorded
/// site, and is let through: there is nothing to compare with, and refusing
/// would strand a meeting in progress across the upgrade.</para>
/// </summary>
public sealed class UnitySiteGuard
{
	private static readonly ILogger Logger = AppLogger.ForContext<UnitySiteGuard>();

	private readonly IConfigurationService _config;
	private readonly IUnitySiteStore _store;
	private readonly RegistrationEventLog _registrationLog;
	private readonly ComplianceEventLog _complianceLog;

	public UnitySiteGuard(
		IConfigurationService config,
		IUnitySiteStore store,
		RegistrationEventLog registrationLog,
		ComplianceEventLog complianceLog)
	{
		_config = config;
		_store = store;
		_registrationLog = registrationLog;
		_complianceLog = complianceLog;
	}

	/// <summary>Compares the recorded site with the configured one. Changes nothing.</summary>
	public async Task<UnitySiteCheck> CheckAsync()
	{
		var unity = await _config.LoadUnityConfigurationAsync().ConfigureAwait(false);
		var recordsFrom = _store.Get();
		var configured = unity.BaseUrl;

		// No configured site is not a different site: the Unity client
		// factory already refuses with "not set up", which is the truer
		// message.
		var changed = !string.IsNullOrWhiteSpace(recordsFrom)
			&& !string.IsNullOrWhiteSpace(configured)
			&& !SameSite(recordsFrom, configured);

		var unsent = _registrationLog.HasPendingEntries()
			|| _complianceLog.HasPendingEntries()
			|| unity.ActiveIntergroupMeetingId.HasValue;

		return new UnitySiteCheck(recordsFrom, configured, changed, unsent);
	}

	/// <summary>Throws <see cref="UnitySiteChangedException"/> unless the records may go to the configured site.</summary>
	public async Task EnsureCanSendAsync()
	{
		var check = await CheckAsync().ConfigureAwait(false);
		if (check.Changed)
		{
			Logger.Warning(
				"Refused to send records from {RecordsFrom} to {ConfiguredSite}",
				check.RecordsFrom, check.ConfiguredSite);
			throw UnitySiteChangedException.Sending(check.RecordsFrom!, check.ConfiguredSite);
		}
	}

	/// <summary>Throws <see cref="UnitySiteChangedException"/> if loading would bury unsent work from another site.</summary>
	public async Task EnsureCanReplaceAsync()
	{
		var check = await CheckAsync().ConfigureAwait(false);
		if (check.Changed && check.HasUnsentWork)
		{
			Logger.Warning(
				"Refused to load from {ConfiguredSite} over unsent records from {RecordsFrom}",
				check.ConfiguredSite, check.RecordsFrom);
			throw UnitySiteChangedException.Replacing(check.RecordsFrom!, check.ConfiguredSite);
		}
	}

	/// <summary>Records the configured site as the one the tablet's records now come from. Call after a load succeeds.</summary>
	public async Task RecordAsync()
	{
		var unity = await _config.LoadUnityConfigurationAsync().ConfigureAwait(false);
		_store.Set(string.IsNullOrWhiteSpace(unity.BaseUrl) ? null : unity.BaseUrl.Trim());
	}

	/// <summary>Forgets the recorded site. For Reset Device, which leaves no records to come from anywhere.</summary>
	public void Forget() => _store.Set(null);

	/// <summary>
	/// Whether two addresses name the same site: the scheme and host in any
	/// case, a default port or none, a trailing slash or none — but the
	/// path exactly, because <c>/amber</c> and the root are different sites
	/// on the same host.
	/// </summary>
	public static bool SameSite(string? a, string? b) =>
		string.Equals(Normalise(a), Normalise(b), StringComparison.Ordinal);

	private static string Normalise(string? site)
	{
		var trimmed = (site ?? string.Empty).Trim().TrimEnd('/');

		return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
			? uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath.TrimEnd('/')
			: trimmed.ToLowerInvariant();
	}
}
