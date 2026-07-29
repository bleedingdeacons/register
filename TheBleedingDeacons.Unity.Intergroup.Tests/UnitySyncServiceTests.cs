using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBleedingDeacons.Unity.Client;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Services;
using Xunit;

namespace TheBleedingDeacons.Unity.Intergroup.Tests;

/// <summary>
/// Exercises <see cref="UnitySyncService.SyncAsync"/> end to end: a real
/// <c>UnityRestSharp</c> over a stub transport feeds the map/sanitise/replace
/// pipeline, which writes into an in-memory SQLite database.
/// </summary>
public sealed class UnitySyncServiceTests : IDisposable
{
	private readonly SqliteConnection _connection;
	private readonly DbContextOptions<UnityDbContext> _options;
	private readonly TestDbContextFactory _factory;

	public UnitySyncServiceTests()
	{
		_connection = new SqliteConnection("DataSource=:memory:");
		_connection.Open();
		_options = new DbContextOptionsBuilder<UnityDbContext>().UseSqlite(_connection).Options;
		using var ctx = new UnityDbContext(_options);
		ctx.Database.EnsureCreated();
		_factory = new TestDbContextFactory(_options);
	}

	public void Dispose() => _connection.Dispose();

	private const string GroupsJson = """
    {"success":true,"data":[
        {"id":1,"title":"Group One","email":"g@x.test","district_id":2,
         "meetings":[{"id":10,"name":"Monday Meeting","day":1,"time":"19:00","is_online":false,
                      "location":{"name":"Church Hall","formatted_address":"1 High St"},"types":["O","D"]}],
         "contacts":[{"name":"Alice","email":"a@x.test","phone":"0700"}]}
    ],"meta":{"total_pages":1}}
    """;

	private const string PositionsJson = """
    {"success":true,"data":[
        {"id":1,"short_description":"Treasurer","long_name":"Intergroup Treasurer","minimum_sobriety":2,"term_years":3}
    ],"meta":{"total_pages":1}}
    """;

	// Member 2 has dangling home_group_id / intergroup_position_id → sanitised to null.
	private const string MembersJson = """
    {"success":true,"data":[
        {"id":1,"anonymous_name":"Bob","is_gsr":true,"home_group_id":1,"intergroup_position_id":1,
         "gdpr_compliance":{"accepted":true,"version":"1.0","method":"app","statement":"ok"}},
        {"id":2,"anonymous_name":"Dangling","home_group_id":999,"intergroup_position_id":888}
    ],"meta":{"total_pages":1}}
    """;

	private const string IntergroupMeetingsJson = """
    {"success":true,"data":[
        {"id":1,"title":"January","date":"2026-01-15",
         "group_attendee_ids":[1,2],"group_attendees":[{"id":1,"name":"Group A"}],
         "officers_attending_ids":[7],"officers_attending":[{"id":7,"name":"Treasurer"}]}
    ],"meta":{"total_pages":1}}
    """;

	private UnitySyncService MakeService(Func<Uri, (HttpStatusCode, string)> responder)
	{
		Task<UnityRestSharp> ClientFactory() =>
			Task.FromResult(new UnityRestSharp("https://unity.test", "int_key", new HttpClient(new StubHttpMessageHandler(responder))));

		return new UnitySyncService(_factory, ClientFactory, NullLogger<UnitySyncService>.Instance);
	}

	private static (HttpStatusCode, string) DefaultResponder(Uri uri)
	{
		var path = uri.AbsolutePath;
		if (path.Contains("/groups", StringComparison.Ordinal)) return (HttpStatusCode.OK, GroupsJson);
		if (path.Contains("/positions", StringComparison.Ordinal)) return (HttpStatusCode.OK, PositionsJson);
		if (path.Contains("/members", StringComparison.Ordinal)) return (HttpStatusCode.OK, MembersJson);
		if (path.Contains("/intergroup-meetings", StringComparison.Ordinal)) return (HttpStatusCode.OK, IntergroupMeetingsJson);
		return (HttpStatusCode.NotFound, """{"success":false,"error":{"code":"nf","message":"not found"}}""");
	}

	[Fact]
	public async Task SyncAsync_ReplacesLocalDataAndReportsCounts()
	{
		var progressReports = new List<SyncProgress>();
		var progress = new Progress<SyncProgress>(progressReports.Add);

		var result = await MakeService(DefaultResponder).SyncAsync(progress: progress);

		Assert.Equal(1, result.Groups);
		Assert.Equal(1, result.Meetings);
		Assert.Equal(1, result.Positions);
		Assert.Equal(2, result.Members);
		Assert.Equal(1, result.Contacts);
		Assert.Equal(1, result.IntergroupMeetings);

		await using var db = new UnityDbContext(_options);
		Assert.Equal(1, await db.Groups.CountAsync());
		Assert.Equal(2, await db.Members.CountAsync());

		// The dangling member's foreign keys were nulled out.
		var dangling = await db.Members.SingleAsync(m => m.Id == 2);
		Assert.Null(dangling.HomeGroupId);
		Assert.Null(dangling.IntergroupPositionId);

		// The good member keeps its valid references and mapped GDPR fields.
		var bob = await db.Members.SingleAsync(m => m.Id == 1);
		Assert.Equal(1, bob.HomeGroupId);
		Assert.True(bob.GdprAccepted);
	}

	[Fact]
	public async Task SyncAsync_PurgesPreviousDataFirst()
	{
		// Pre-seed stale data that the sync must clear.
		await using (var seed = new UnityDbContext(_options))
		{
			seed.Groups.Add(new Entities.Group { Id = 77, Name = "Stale" });
			await seed.SaveChangesAsync();
		}

		await MakeService(DefaultResponder).SyncAsync();

		await using var db = new UnityDbContext(_options);
		Assert.False(await db.Groups.AnyAsync(g => g.Id == 77));
	}

	[Fact]
	public async Task SyncAsync_FollowsPaginationAcrossPages()
	{
		var groupsPage1 = """
        {"success":true,"data":[{"id":1,"title":"One"}],"meta":{"total_pages":2}}
        """;
		var groupsPage2 = """
        {"success":true,"data":[{"id":2,"title":"Two"}],"meta":{"total_pages":2}}
        """;

		(HttpStatusCode, string) Responder(Uri uri)
		{
			var path = uri.AbsolutePath;
			var query = uri.Query;
			if (path.Contains("/groups", StringComparison.Ordinal))
				return (HttpStatusCode.OK, query.Contains("page=2", StringComparison.Ordinal) ? groupsPage2 : groupsPage1);
			return DefaultResponder(uri);
		}

		var result = await MakeService(Responder).SyncAsync();

		Assert.Equal(2, result.Groups); // both pages combined
	}

	[Fact]
	public async Task SyncAsync_ThrowsWhenAnEndpointFails()
	{
		(HttpStatusCode, string) Responder(Uri uri) =>
			uri.AbsolutePath.Contains("/groups", StringComparison.Ordinal)
				? (HttpStatusCode.OK, """{"success":false,"error":{"code":"err","message":"boom"}}""")
				: DefaultResponder(uri);

		await Assert.ThrowsAsync<InvalidOperationException>(() => MakeService(Responder).SyncAsync());
	}

	private sealed class TestDbContextFactory(DbContextOptions<UnityDbContext> options)
		: IDbContextFactory<UnityDbContext>
	{
		public UnityDbContext CreateDbContext() => new(options);
	}
}
