using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Services;
using Xunit;

namespace TheBleedingDeacons.Unity.Intergroup.Tests;

/// <summary>
/// Covers UnityDbContext behaviours (purge, the Updated-timestamp stamping and
/// its suppression, the synchronous SaveChanges path), the SyncProgress record
/// and the EntitySnapshot entity.
/// </summary>
public sealed class DbContextTests : IDisposable
{
	private readonly SqliteConnection _connection;
	private readonly DbContextOptions<UnityDbContext> _options;

	public DbContextTests()
	{
		_connection = new SqliteConnection("DataSource=:memory:");
		_connection.Open();
		_options = new DbContextOptionsBuilder<UnityDbContext>().UseSqlite(_connection).Options;
		using var ctx = new UnityDbContext(_options);
		ctx.Database.EnsureCreated();
	}

	public void Dispose() => _connection.Dispose();

	[Fact]
	public async Task PurgeDatabaseAsync_ClearsEveryTable()
	{
		await using (var seed = new UnityDbContext(_options))
		{
			seed.Groups.Add(new Group { Id = 1, Name = "G" });
			seed.Positions.Add(new Position { Id = 1, ShortDescription = "P" });
			seed.Members.Add(new Member { Id = 1, AnonymousName = "M" });
			seed.IntergroupMeetings.Add(new IntergroupMeeting { Id = 1, Title = "IM" });
			seed.EntitySnapshots.Add(new EntitySnapshot { EntityType = "Group", EntityKey = 1, JsonData = "{}", SnapshotUtc = DateTime.UtcNow });
			await seed.SaveChangesAsync();
		}

		await using (var ctx = new UnityDbContext(_options))
		{
			await ctx.PurgeDatabaseAsync();
		}

		await using var check = new UnityDbContext(_options);
		Assert.Equal(0, await check.Groups.CountAsync());
		Assert.Equal(0, await check.Members.CountAsync());
		Assert.Equal(0, await check.EntitySnapshots.CountAsync());
	}

	[Fact]
	public void SaveChanges_StampsUpdated_WhenNotSuppressed()
	{
		using var ctx = new UnityDbContext(_options);
		var group = new Group { Id = 5, Name = "Stamped" };
		ctx.Groups.Add(group);

		ctx.SaveChanges(); // synchronous path

		Assert.NotNull(group.Updated);
	}

	[Fact]
	public async Task SaveChanges_DoesNotStampUpdated_WhenSuppressed()
	{
		await using var ctx = new UnityDbContext(_options) { SuppressUpdatedStamp = true };
		var group = new Group { Id = 6, Name = "Unstamped" };
		ctx.Groups.Add(group);

		await ctx.SaveChangesAsync();

		Assert.Null(group.Updated);
	}

	[Fact]
	public void SyncProgress_DefaultsToIndeterminate()
	{
		var indeterminate = new SyncProgress(SyncStage.WritingDatabase, "Saving…");
		Assert.Equal(0, indeterminate.Current);
		Assert.Null(indeterminate.Total);

		var determinate = new SyncProgress(SyncStage.Fetching, "Fetching groups (page 1 of 3)", Current: 1, Total: 3);
		Assert.Equal(1, determinate.Current);
		Assert.Equal(3, determinate.Total);
	}

	[Fact]
	public async Task EntitySnapshot_PersistsItsColumns()
	{
		var when = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
		await using (var seed = new UnityDbContext(_options))
		{
			seed.EntitySnapshots.Add(new EntitySnapshot { EntityType = "Member", EntityKey = 42, JsonData = """{"id":42}""", SnapshotUtc = when });
			await seed.SaveChangesAsync();
		}

		await using var ctx = new UnityDbContext(_options);
		var snapshot = await ctx.EntitySnapshots.SingleAsync();
		Assert.Equal("Member", snapshot.EntityType);
		Assert.Equal(42, snapshot.EntityKey);
		Assert.Equal("""{"id":42}""", snapshot.JsonData);
	}
}
