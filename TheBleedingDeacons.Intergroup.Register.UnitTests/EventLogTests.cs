using TheBleedingDeacons.Intergroup.Register.Services;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Register.UnitTests;

/// <summary>
/// The two append-only logs are the app's crash-durability layer: if SQLite is
/// lost between a registration and the end-of-meeting reconcile, these files
/// are what rebuilds it. Their contract is upsert-by-entity (last line wins)
/// and tolerance of a torn final line, which is exactly what a power loss
/// mid-write produces.
///
/// <para>Both classes already carried an internal constructor taking an
/// explicit path, added for non-MAUI hosts. These tests are its first
/// consumer — the default path resolver reaches for
/// <c>FileSystem.AppDataDirectory</c>, which throws in a console host.</para>
/// </summary>
public sealed class RegistrationEventLogTests : IDisposable
{
	private readonly string _dir;
	private readonly string _logPath;

	public RegistrationEventLogTests()
	{
		_dir = Path.Combine(Path.GetTempPath(), "register-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_dir);
		_logPath = Path.Combine(_dir, "registrations.log");
	}

	public void Dispose()
	{
		try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
	}

	private RegistrationEventLog NewLog() => new(_logPath);

	[Fact]
	public void HasPendingEntries_IsFalseBeforeAnythingIsWritten()
	{
		Assert.False(NewLog().HasPendingEntries());
	}

	[Fact]
	public async Task AppendGroup_MakesTheEntryReadableWithItsProxyDetails()
	{
		var log = NewLog();

		await log.AppendGroupAsync(12, registered: true, gsrProxy: true, gsrProxyName: "Alex");

		Assert.True(log.HasPendingEntries());
		var states = await log.ReadLatestStatesAsync();
		var entry = states[(RegistrationEventLog.EntityKind.Group, 12)];

		Assert.True(entry.Registered);
		Assert.True(entry.GsrProxy);
		Assert.Equal("Alex", entry.GsrProxyName);
	}

	[Fact]
	public async Task AppendPosition_LeavesTheGroupOnlyFieldsUnset()
	{
		var log = NewLog();

		await log.AppendPositionAsync(88, registered: true);

		var entry = (await log.ReadLatestStatesAsync())[(RegistrationEventLog.EntityKind.Position, 88)];

		Assert.True(entry.Registered);
		Assert.Null(entry.GsrProxy);
		Assert.Null(entry.GsrProxyName);
	}

	[Fact]
	public async Task ReadLatestStates_KeepsOnlyTheMostRecentLinePerEntity()
	{
		// Upsert semantics: a GSR who registers, unregisters and re-registers
		// must end up registered exactly once, not counted three times.
		var log = NewLog();
		await log.AppendGroupAsync(12, registered: true, gsrProxy: false, gsrProxyName: null);
		await log.AppendGroupAsync(12, registered: false, gsrProxy: false, gsrProxyName: null);
		await log.AppendGroupAsync(12, registered: true, gsrProxy: true, gsrProxyName: "Sam");

		var states = await log.ReadLatestStatesAsync();

		Assert.Single(states);
		var entry = states[(RegistrationEventLog.EntityKind.Group, 12)];
		Assert.True(entry.Registered);
		Assert.Equal("Sam", entry.GsrProxyName);
	}

	[Fact]
	public async Task ReadLatestStates_KeepsGroupsAndPositionsWithTheSameIdApart()
	{
		var log = NewLog();
		await log.AppendGroupAsync(5, registered: true, gsrProxy: false, gsrProxyName: null);
		await log.AppendPositionAsync(5, registered: false);

		var states = await log.ReadLatestStatesAsync();

		Assert.Equal(2, states.Count);
		Assert.True(states[(RegistrationEventLog.EntityKind.Group, 5)].Registered);
		Assert.False(states[(RegistrationEventLog.EntityKind.Position, 5)].Registered);
	}

	[Fact]
	public async Task ReadLatestStates_SkipsATornFinalLineAndKeepsTheRest()
	{
		// The whole point of newline-delimited JSON: a half-written last
		// record costs that record, not the meeting.
		var log = NewLog();
		await log.AppendGroupAsync(1, registered: true, gsrProxy: false, gsrProxyName: null);
		await log.AppendGroupAsync(2, registered: true, gsrProxy: false, gsrProxyName: null);
		await File.AppendAllTextAsync(_logPath, "{\"timestampUtc\":\"2026-08-08T10:0");

		var states = await log.ReadLatestStatesAsync();

		Assert.Equal(2, states.Count);
		Assert.True(states[(RegistrationEventLog.EntityKind.Group, 1)].Registered);
		Assert.True(states[(RegistrationEventLog.EntityKind.Group, 2)].Registered);
	}

	[Fact]
	public async Task ReadLatestStates_ReturnsEmptyWhenTheFileIsMissing()
	{
		Assert.Empty(await NewLog().ReadLatestStatesAsync());
	}

	[Fact]
	public async Task Purge_DeletesTheLogSoTheNextMeetingStartsClean()
	{
		var log = NewLog();
		await log.AppendGroupAsync(1, registered: true, gsrProxy: false, gsrProxyName: null);

		await log.PurgeAsync();

		Assert.False(File.Exists(_logPath));
		Assert.False(log.HasPendingEntries());
		Assert.Empty(await log.ReadLatestStatesAsync());
	}

	[Fact]
	public async Task Purge_IsSafeWhenThereIsNoLog()
	{
		await NewLog().PurgeAsync();
	}

	[Fact]
	public async Task Append_RecreatesTheLogAfterAPurge()
	{
		var log = NewLog();
		await log.AppendGroupAsync(1, registered: true, gsrProxy: false, gsrProxyName: null);
		await log.PurgeAsync();

		await log.AppendGroupAsync(2, registered: true, gsrProxy: false, gsrProxyName: null);

		var states = await log.ReadLatestStatesAsync();
		Assert.Single(states);
		Assert.True(states.ContainsKey((RegistrationEventLog.EntityKind.Group, 2)));
	}

	[Fact]
	public async Task Entries_SurviveANewInstanceOverTheSameFile()
	{
		// Standing in for a process restart: the point of the log is that a
		// fresh process can read what the dead one wrote.
		await using (var writer = NewLog())
		{
			await writer.AppendPositionAsync(3, registered: true);
		}

		await using var reader = NewLog();
		var states = await reader.ReadLatestStatesAsync();

		Assert.True(states[(RegistrationEventLog.EntityKind.Position, 3)].Registered);
	}

	[Fact]
	public async Task Append_IsSafeUnderConcurrentWriters()
	{
		// ViewModel commands can fire concurrently on the thread pool, so the
		// write lock is not paranoia — every line must land intact.
		var log = NewLog();

		await Task.WhenAll(Enumerable.Range(1, 25)
			.Select(id => log.AppendPositionAsync(id, registered: true)));

		var states = await log.ReadLatestStatesAsync();
		Assert.Equal(25, states.Count);
	}
}

/// <summary>
/// Same contract as <see cref="RegistrationEventLogTests"/>, keyed by member
/// rather than by (kind, id). This log backs the GDPR audit trail, so a lost
/// or mis-collapsed entry is a compliance problem rather than a UX one.
/// </summary>
public sealed class ComplianceEventLogTests : IDisposable
{
	private readonly string _dir;
	private readonly string _logPath;

	public ComplianceEventLogTests()
	{
		_dir = Path.Combine(Path.GetTempPath(), "register-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_dir);
		_logPath = Path.Combine(_dir, "compliance.log");
	}

	public void Dispose()
	{
		try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
	}

	private ComplianceEventLog NewLog() => new(_logPath);

	[Fact]
	public async Task AppendAcceptance_RecordsEveryAuditField()
	{
		var log = NewLog();
		var acceptedAt = new DateTime(2026, 3, 4, 19, 30, 0, DateTimeKind.Utc);

		await log.AppendAcceptanceAsync(77, acceptedAt, "2.1", "register-app", "We keep your details safe.", policyId: 4211);

		var entry = (await log.ReadLatestStatesAsync())[77];

		Assert.True(entry.Accepted);
		Assert.Equal(acceptedAt, entry.AcceptedAt);
		Assert.Equal("2.1", entry.Version);
		Assert.Equal("register-app", entry.Method);
		Assert.Equal(4211, entry.PolicyId);
	}

	[Fact]
	public async Task AppendAcceptance_AcceptsAnUnknownPolicyId()
	{
		// A device that has never synced a policy still records the consent.
		var log = NewLog();

		await log.AppendAcceptanceAsync(77, DateTime.UtcNow, "2.1", "register-app", null, policyId: null);

		Assert.Null((await log.ReadLatestStatesAsync())[77].PolicyId);
	}

	[Fact]
	public async Task AppendRevocation_ClearsTheAcceptanceMetadata()
	{
		var log = NewLog();

		await log.AppendRevocationAsync(77, DateTime.UtcNow);

		var entry = (await log.ReadLatestStatesAsync())[77];
		Assert.False(entry.Accepted);
		Assert.Null(entry.Version);
		Assert.Null(entry.Method);
		Assert.Null(entry.Statement);
	}

	[Fact]
	public async Task ReadLatestStates_LetsARevocationSupersedeAnEarlierAcceptance()
	{
		var log = NewLog();
		await log.AppendAcceptanceAsync(77, DateTime.UtcNow, "2.1", "register-app", null, 4211);
		await log.AppendRevocationAsync(77, DateTime.UtcNow);

		var states = await log.ReadLatestStatesAsync();

		Assert.Single(states);
		Assert.False(states[77].Accepted);
	}

	[Fact]
	public async Task ReadLatestStates_TracksMembersIndependently()
	{
		var log = NewLog();
		await log.AppendAcceptanceAsync(1, DateTime.UtcNow, "2.1", "register-app", null, 4211);
		await log.AppendRevocationAsync(2, DateTime.UtcNow);

		var states = await log.ReadLatestStatesAsync();

		Assert.True(states[1].Accepted);
		Assert.False(states[2].Accepted);
	}

	[Fact]
	public async Task ReadLatestStates_SkipsATornFinalLine()
	{
		var log = NewLog();
		await log.AppendAcceptanceAsync(1, DateTime.UtcNow, "2.1", "register-app", null, 4211);
		await File.AppendAllTextAsync(_logPath, "{\"timestampUtc\":\"2026-08");

		var states = await log.ReadLatestStatesAsync();

		Assert.Single(states);
		Assert.True(states[1].Accepted);
	}

	[Fact]
	public async Task Purge_DeletesTheLog()
	{
		var log = NewLog();
		await log.AppendAcceptanceAsync(1, DateTime.UtcNow, "2.1", "register-app", null, 4211);

		await log.PurgeAsync();

		Assert.False(log.HasPendingEntries());
		Assert.Empty(await log.ReadLatestStatesAsync());
	}
}
