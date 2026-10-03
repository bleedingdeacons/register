using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Tests.Support;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using EntityKind = TheBleedingDeacons.Intergroup.Register.Services.RegistrationEventLog.EntityKind;

namespace TheBleedingDeacons.Intergroup.Register.Tests;

/// <summary>
/// The registration log as a file on a tablet that crashed: torn lines,
/// junk, nothing at all. That the latest line wins and a replay rebuilds
/// the meeting is the specification's to say.
/// </summary>
public sealed class RegistrationEventLogTests : IDisposable
{
	private readonly TempDirectory _dir = new();
	private readonly RegistrationEventLog _log;

	public RegistrationEventLogTests() => _log = new RegistrationEventLog(_dir.File(RegistrationEventLog.FileName));

	public void Dispose()
	{
		_log.DisposeAsync().AsTask().GetAwaiter().GetResult();
		_dir.Dispose();
	}

	[Fact]
	public async Task NoFileIsNoState()
	{
		Assert.False(_log.HasPendingEntries());
		Assert.Empty(await _log.ReadLatestStatesAsync());
	}

	[Fact]
	public async Task AnEmptyFileIsNoState()
	{
		await File.WriteAllTextAsync(_log.LogPath, string.Empty);

		Assert.False(_log.HasPendingEntries());
		Assert.Empty(await _log.ReadLatestStatesAsync());
	}

	[Fact]
	public async Task ATornLastLineCostsOnlyThatLine()
	{
		await _log.AppendGroupAsync(1, registered: true, gsrProxy: false, gsrProxyName: null);
		await _log.AppendPositionAsync(2, registered: true);
		await File.AppendAllTextAsync(_log.LogPath, "{\"timestampUtc\":\"2026-10-03T10:00:00Z\",\"kind\":0,\"enti");

		var states = await _log.ReadLatestStatesAsync();

		Assert.Equal(2, states.Count);
	}

	[Fact]
	public async Task JunkInTheMiddleDoesNotLoseWhatComesAfterIt()
	{
		await _log.AppendGroupAsync(1, registered: true, gsrProxy: false, gsrProxyName: null);
		await File.AppendAllTextAsync(_log.LogPath, "not json at all\n\n   \n");
		await _log.AppendGroupAsync(1, registered: false, gsrProxy: false, gsrProxyName: null);

		var states = await _log.ReadLatestStatesAsync();

		Assert.False(Assert.Single(states).Value.Registered);
	}

	/// <summary>
	/// Found, not chosen: the kind is written as a number (there is no
	/// string enum converter, whatever the comment on EntityKind says), so
	/// a kind written as text is unreadable and skipped, and a number with
	/// no name is read and then ignored by replay.
	/// </summary>
	[Fact]
	public async Task AKindWrittenAsTextIsSkippedAndAnUnknownNumberIsIgnoredByReplay()
	{
		await File.WriteAllTextAsync(_log.LogPath,
			"{\"timestampUtc\":\"2026-10-03T10:00:00Z\",\"kind\":\"Group\",\"entityId\":1,\"registered\":true}\n" +
			"{\"timestampUtc\":\"2026-10-03T10:00:00Z\",\"kind\":7,\"entityId\":2,\"registered\":true}\n");

		var states = await _log.ReadLatestStatesAsync();
		Assert.Equal([((EntityKind)7, 2)], states.Keys);

		using var db = Databases.Unity();
		var result = await _log.ReplayIntoDatabaseAsync(db.CreateDbContext());
		Assert.Equal(new RegistrationEventLog.ReplayResult(0, 0, 0), result);
	}

	[Fact]
	public async Task ReplaySkipsEntitiesTheSyncRemoved()
	{
		using var db = Databases.Unity();
		await using (var seed = db.CreateDbContext())
		{
			seed.Groups.Add(new Group { Id = 1, Name = "Monday Step" });
			await seed.SaveChangesAsync();
		}

		await _log.AppendGroupAsync(1, registered: true, gsrProxy: true, gsrProxyName: "Sam");
		await _log.AppendGroupAsync(404, registered: true, gsrProxy: false, gsrProxyName: null);
		await _log.AppendPositionAsync(405, registered: true);

		await using var context = db.CreateDbContext();
		var result = await _log.ReplayIntoDatabaseAsync(context);

		Assert.Equal(new RegistrationEventLog.ReplayResult(GroupsApplied: 1, PositionsApplied: 0, MissingEntities: 2), result);
	}

	[Fact]
	public async Task AProxyNameWithoutTheProxyFlagIsDropped()
	{
		using var db = Databases.Unity();
		await using (var seed = db.CreateDbContext())
		{
			seed.Groups.Add(new Group { Id = 1, Name = "Monday Step" });
			await seed.SaveChangesAsync();
		}

		await _log.AppendGroupAsync(1, registered: true, gsrProxy: false, gsrProxyName: "Sam");
		await using (var context = db.CreateDbContext())
		{
			await _log.ReplayIntoDatabaseAsync(context);
		}

		await using var check = db.CreateDbContext();
		var group = await check.Groups.FindAsync(1);
		Assert.True(group!.Registered);
		Assert.Null(group.GsrProxyName);
	}

	[Fact]
	public async Task PurgingNothingIsFine()
	{
		await _log.PurgeAsync();

		Assert.False(File.Exists(_log.LogPath));
	}

	[Fact]
	public async Task AnAppendAfterAPurgeStartsAFreshFile()
	{
		await _log.AppendPositionAsync(1, registered: true);
		await _log.PurgeAsync();
		await _log.AppendPositionAsync(2, registered: true);

		Assert.Equal([(EntityKind.Position, 2)], (await _log.ReadLatestStatesAsync()).Keys);
	}

	[Fact]
	public async Task AnAppendThatCannotBeWrittenDoesNotThrow()
	{
		// A directory where the file should be: every open fails.
		Directory.CreateDirectory(_log.LogPath);

		await _log.AppendPositionAsync(1, registered: true);
	}
}

/// <summary>The compliance log, under the same conditions.</summary>
public sealed class ComplianceEventLogTests : IDisposable
{
	private static readonly DateTime At = new(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc);

	private readonly TempDirectory _dir = new();
	private readonly ComplianceEventLog _log;

	public ComplianceEventLogTests() => _log = new ComplianceEventLog(_dir.File(ComplianceEventLog.FileName));

	public void Dispose()
	{
		_log.DisposeAsync().AsTask().GetAwaiter().GetResult();
		_dir.Dispose();
	}

	[Fact]
	public async Task NoFileIsNoState() =>
		Assert.Empty(await _log.ReadLatestStatesAsync());

	[Fact]
	public async Task ATornLastLineCostsOnlyThatLine()
	{
		await _log.AppendAcceptanceAsync(1, At, "2.1", "register-app", null, 42);
		await File.AppendAllTextAsync(_log.LogPath, "{\"timestampUtc\":\"2026-10-");

		Assert.True(Assert.Single(await _log.ReadLatestStatesAsync()).Value.Accepted);
	}

	[Fact]
	public async Task ARevocationAfterAnAcceptanceWins()
	{
		await _log.AppendAcceptanceAsync(1, At, "2.1", "register-app", null, 42);
		await _log.AppendRevocationAsync(1, At.AddMinutes(5));

		var entry = Assert.Single(await _log.ReadLatestStatesAsync()).Value;

		Assert.False(entry.Accepted);
		Assert.Null(entry.Version);
	}

	[Fact]
	public async Task ReplaySkipsMembersTheSyncRemoved()
	{
		using var db = Databases.Unity();
		await _log.AppendAcceptanceAsync(404, At, "2.1", "register-app", null, 42);

		await using var context = db.CreateDbContext();
		var result = await _log.ReplayIntoDatabaseAsync(context);

		Assert.Equal(new ComplianceEventLog.ReplayResult(Applied: 0, MissingEntities: 1), result);
	}
}
