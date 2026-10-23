using TheBleedingDeacons.Intergroup.Register.Exceptions;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Tests.Support;

namespace TheBleedingDeacons.Intergroup.Register.Tests;

/// <summary>
/// The guard that keeps one Unity site's records from going to another:
/// what counts as the same site, and when a load or a finish is refused.
/// That a refused finish sends nothing is the specification's to say.
/// </summary>
public sealed class UnitySiteGuardTests : IDisposable
{
	private const string Amber = "https://aa-bristol.org/amber";
	private const string Live = "https://aa-bristol.org";

	private readonly TempDirectory _dir = new();
	private readonly FakeConfigurationService _config = new();
	private readonly Store _store = new();
	private readonly RegistrationEventLog _registrations;
	private readonly ComplianceEventLog _compliance;

	public UnitySiteGuardTests()
	{
		_registrations = new RegistrationEventLog(_dir.File(RegistrationEventLog.FileName));
		_compliance = new ComplianceEventLog(_dir.File(ComplianceEventLog.FileName));
		_config.Unity.ActiveIntergroupMeetingId = null;
	}

	public void Dispose()
	{
		_registrations.DisposeAsync().AsTask().GetAwaiter().GetResult();
		_compliance.DisposeAsync().AsTask().GetAwaiter().GetResult();
		_dir.Dispose();
	}

	private UnitySiteGuard Guard() => new(_config, _store, _registrations, _compliance);

	private void From(string recorded, string configured)
	{
		_store.Site = recorded;
		_config.Unity.BaseUrl = configured;
	}

	[Theory]
	[InlineData("https://aa-bristol.org/amber", "https://aa-bristol.org/amber/")]
	[InlineData("https://aa-bristol.org/amber", "HTTPS://AA-Bristol.ORG/amber")]
	[InlineData("https://aa-bristol.org", "https://aa-bristol.org:443")]
	[InlineData(" https://aa-bristol.org ", "https://aa-bristol.org")]
	[InlineData("aa-bristol.org", "AA-BRISTOL.ORG")]
	public void TheSameSiteWrittenDifferentlyIsTheSameSite(string a, string b) =>
		Assert.True(UnitySiteGuard.SameSite(a, b));

	[Theory]
	[InlineData("https://aa-bristol.org/amber", "https://aa-bristol.org")]
	[InlineData("https://aa-bristol.org/amber", "https://aa-bristol.org/Amber")]
	[InlineData("https://aa-bristol.org", "http://aa-bristol.org")]
	[InlineData("https://aa-bristol.org", "https://aa-bristol.org:8443")]
	[InlineData("https://aa-bristol.org", "https://www.aa-bristol.org")]
	public void APathSchemePortOrHostMakesADifferentSite(string a, string b) =>
		Assert.False(UnitySiteGuard.SameSite(a, b));

	[Fact]
	public async Task NothingRecordedIsNotAChange()
	{
		_config.Unity.BaseUrl = Live;

		var check = await Guard().CheckAsync();

		Assert.False(check.Changed);
		await Guard().EnsureCanSendAsync();
	}

	[Fact]
	public async Task NoConfiguredSiteIsNotAChange()
	{
		From(Amber, string.Empty);

		Assert.False((await Guard().CheckAsync()).Changed);
	}

	[Fact]
	public async Task AFinishToAnotherSiteIsRefusedEvenWithNothingPending()
	{
		From(Amber, Live);

		var ex = await Assert.ThrowsAsync<UnitySiteChangedException>(() => Guard().EnsureCanSendAsync());

		Assert.Equal(Amber, ex.RecordsFrom);
		Assert.Equal(Live, ex.ConfiguredSite);
		Assert.Contains(Amber, ex.Message, StringComparison.Ordinal);
		Assert.Contains(Live, ex.Message, StringComparison.Ordinal);
		Assert.Contains("Reset Device", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ALoadWithNothingPendingSimplyReplacesTheRecords()
	{
		From(Amber, Live);

		await Guard().EnsureCanReplaceAsync();
	}

	[Fact]
	public async Task ALoadOverPendingRegistrationsFromAnotherSiteIsRefused()
	{
		From(Amber, Live);
		await _registrations.AppendGroupAsync(12, registered: true, gsrProxy: false, gsrProxyName: null);

		await Assert.ThrowsAsync<UnitySiteChangedException>(() => Guard().EnsureCanReplaceAsync());
	}

	[Fact]
	public async Task ALoadOverPendingConsentFromAnotherSiteIsRefused()
	{
		From(Amber, Live);
		await File.WriteAllTextAsync(_compliance.LogPath, "{}\n");

		await Assert.ThrowsAsync<UnitySiteChangedException>(() => Guard().EnsureCanReplaceAsync());
	}

	[Fact]
	public async Task ALoadDuringAMeetingFromAnotherSiteIsRefused()
	{
		From(Amber, Live);
		_config.Unity.ActiveIntergroupMeetingId = 77;

		await Assert.ThrowsAsync<UnitySiteChangedException>(() => Guard().EnsureCanReplaceAsync());
	}

	[Fact]
	public async Task ALoadOverPendingWorkFromTheSameSiteGoesAhead()
	{
		From(Amber, Amber + "/");
		await _registrations.AppendGroupAsync(12, registered: true, gsrProxy: false, gsrProxyName: null);

		await Guard().EnsureCanReplaceAsync();
	}

	[Fact]
	public async Task RecordingTakesTheConfiguredSite()
	{
		From(Amber, " " + Live + " ");

		await Guard().RecordAsync();

		Assert.Equal(Live, _store.Site);
	}

	[Fact]
	public async Task RecordingWithNoSiteForgets()
	{
		From(Amber, string.Empty);

		await Guard().RecordAsync();

		Assert.Null(_store.Site);
	}

	[Fact]
	public void ForgettingLeavesNothingRecorded()
	{
		_store.Site = Amber;

		Guard().Forget();

		Assert.Null(_store.Site);
	}

	private sealed class Store : IUnitySiteStore
	{
		public string? Site { get; set; }

		public string? Get() => Site;

		public void Set(string? site) => Site = site;
	}
}
