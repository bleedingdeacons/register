using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Repositories;
using Xunit;

namespace TheBleedingDeacons.Unity.Intergroup.Tests;

/// <summary>
/// Exercises the Meeting, Member, Position and IntergroupMeeting repositories
/// against a real in-memory SQLite database so the EF queries (ordering,
/// filtering, includes, search) are covered end to end.
/// </summary>
public sealed class RepositoryTests : IDisposable
{
	private readonly SqliteConnection _connection;
	private readonly DbContextOptions<UnityDbContext> _options;
	private readonly TestDbContextFactory _factory;

	public RepositoryTests()
	{
		_connection = new SqliteConnection("DataSource=:memory:");
		_connection.Open();

		_options = new DbContextOptionsBuilder<UnityDbContext>()
			.UseSqlite(_connection)
			.Options;

		using var ctx = new UnityDbContext(_options);
		ctx.Database.EnsureCreated();

		_factory = new TestDbContextFactory(_options);
	}

	public void Dispose() => _connection.Dispose();

	private async Task SeedAsync(params object[] entities)
	{
		await using var ctx = new UnityDbContext(_options);
		ctx.AddRange(entities);
		await ctx.SaveChangesAsync();
	}

	// ── MeetingRepository (takes a DbContext directly) ───────────────────────

	private MeetingRepository Meetings() => new(new UnityDbContext(_options));

	[Fact]
	public async Task Meetings_GetAll_OrdersByDayThenTime()
	{
		await SeedAsync(
			new Meeting { Id = 1, Name = "Late Mon", Day = 1, Time = "20:00" },
			new Meeting { Id = 2, Name = "Early Mon", Day = 1, Time = "07:00" },
			new Meeting { Id = 3, Name = "Sun", Day = 0, Time = "10:00" });

		var result = await Meetings().GetAllAsync();

		Assert.Equal(new[] { "Sun", "Early Mon", "Late Mon" }, result.Select(m => m.Name));
	}

	[Fact]
	public async Task Meetings_GetById_HitAndMiss()
	{
		await SeedAsync(new Meeting { Id = 5, Name = "Serenity", Day = 2 });

		Assert.Equal("Serenity", (await Meetings().GetByIdAsync(5))?.Name);
		Assert.Null(await Meetings().GetByIdAsync(999));
	}

	[Fact]
	public async Task Meetings_GetByGroupId_And_GetByDay()
	{
		await SeedAsync(
			new Group { Id = 1, Name = "G1" },
			new Group { Id = 2, Name = "G2" },
			new Meeting { Id = 1, Name = "A", Day = 3, Time = "09:00", GroupId = 1 },
			new Meeting { Id = 2, Name = "B", Day = 3, Time = "10:00", GroupId = 2 });

		Assert.Single(await Meetings().GetByGroupIdAsync(1));
		Assert.Equal(2, (await Meetings().GetByDayAsync(3)).Count);
	}

	[Fact]
	public async Task Meetings_GetOnline_And_Search()
	{
		await SeedAsync(
			new Meeting { Id = 1, Name = "Online One", Day = 1, IsOnline = true },
			new Meeting { Id = 2, Name = "In Person", Day = 1, IsOnline = false, LocationName = "Hall match" });

		Assert.Single(await Meetings().GetOnlineMeetingsAsync());
		Assert.Single(await Meetings().SearchAsync("match"));
	}

	// ── MemberRepository ─────────────────────────────────────────────────────

	[Fact]
	public async Task Members_GetAll_GetById_Gsrs_ByGroup_ByPosition_Search()
	{
		await SeedAsync(
			new Group { Id = 1, Name = "Home" },
			new Position { Id = 1, ShortDescription = "Treasurer" });
		await SeedAsync(
			new Member { Id = 1, AnonymousName = "Bob", IsGsr = true, HomeGroupId = 1, Email = "bob@match.test" },
			new Member { Id = 2, AnonymousName = "Alice", IsGsr = false, IntergroupPositionId = 1 });

		var repo = new MemberRepository(_factory);

		Assert.Equal(new[] { "Alice", "Bob" }, (await repo.GetAllAsync()).Select(m => m.AnonymousName));
		Assert.Equal("Bob", (await repo.GetByIdAsync(1))?.AnonymousName);
		Assert.Null(await repo.GetByIdAsync(99));
		Assert.Single(await repo.GetGsrsAsync());
		Assert.Single(await repo.GetByHomeGroupIdAsync(1));
		Assert.Single(await repo.GetByPositionIdAsync(1));
		Assert.Single(await repo.SearchAsync("match"));
	}

	// ── PositionRepository ───────────────────────────────────────────────────

	[Fact]
	public async Task Positions_All_ById_Holders_Filled_Vacant_Search()
	{
		await SeedAsync(
			new Position { Id = 1, ShortDescription = "Treasurer", LongName = "Intergroup Treasurer" },
			new Position { Id = 2, ShortDescription = "Secretary" });
		await SeedAsync(new Member { Id = 1, AnonymousName = "Bob", IntergroupPositionId = 1 });

		var repo = new PositionRepository(_factory);

		Assert.Equal(2, (await repo.GetAllAsync()).Count);
		Assert.Equal("Treasurer", (await repo.GetByIdAsync(1))?.ShortDescription);
		Assert.Null(await repo.GetByIdAsync(99));
		Assert.Single((await repo.GetByIdWithHoldersAsync(1))!.Holders);
		Assert.Single(await repo.GetFilledPositionsAsync());
		Assert.Single(await repo.GetVacantPositionsAsync());
		Assert.Single(await repo.SearchAsync("Intergroup"));
	}

	// ── IntergroupMeetingRepository ──────────────────────────────────────────

	[Fact]
	public async Task IntergroupMeetings_All_ById_DateRange_Search()
	{
		await SeedAsync(
			new IntergroupMeeting { Id = 1, Title = "January", Date = "2026-01-15", GroupAttendeeNames = "Group A" },
			new IntergroupMeeting { Id = 2, Title = "March", Date = "2026-03-15", OfficerAttendeeNames = "Treasurer match" },
			new IntergroupMeeting { Id = 3, Title = "June", Date = "2026-06-15" });

		var repo = new IntergroupMeetingRepository(_factory);

		// Ordered by date descending.
		Assert.Equal(new[] { "June", "March", "January" }, (await repo.GetAllAsync()).Select(m => m.Title));
		Assert.Equal("March", (await repo.GetByIdAsync(2))?.Title);
		Assert.Null(await repo.GetByIdAsync(99));
		Assert.Equal(2, (await repo.GetByDateRangeAsync("2026-01-01", "2026-04-01")).Count);
		Assert.Single(await repo.SearchAsync("match"));
	}

	private sealed class TestDbContextFactory(DbContextOptions<UnityDbContext> options)
		: IDbContextFactory<UnityDbContext>
	{
		public UnityDbContext CreateDbContext() => new(options);
	}
}
