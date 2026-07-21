using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Repositories;
using Xunit;

namespace TheBleedingDeacons.Unity.Intergroup.Tests;

/// <summary>
/// Exercises <see cref="GroupRepository"/> against a real (in-memory SQLite)
/// database so the EF queries — ordering, filtering, includes — are covered.
/// Group IDs are assigned explicitly because they originate from the Unity API
/// (the key is not store-generated).
/// </summary>
public sealed class GroupRepositoryTests : IDisposable
{
    // A single open connection keeps the in-memory database alive for the
    // lifetime of the test; every context created by the factory shares it.
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<UnityDbContext> _options;
    private readonly GroupRepository _repository;

    public GroupRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<UnityDbContext>()
            .UseSqlite(_connection)
            .Options;

        using (var ctx = new UnityDbContext(_options))
        {
            ctx.Database.EnsureCreated();
        }

        _repository = new GroupRepository(new TestDbContextFactory(_options));
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task GetAllAsync_ReturnsAllGroupsOrderedByName()
    {
        await SeedAsync(
            new Group { Id = 1, Name = "Zebra" },
            new Group { Id = 2, Name = "Alpha" },
            new Group { Id = 3, Name = "Mike" });

        var result = await _repository.GetAllAsync();

        Assert.Equal(new[] { "Alpha", "Mike", "Zebra" }, result.Select(g => g.Name));
    }

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsGroup()
    {
        await SeedAsync(new Group { Id = 42, Name = "Serenity" });

        var result = await _repository.GetByIdAsync(42);

        Assert.NotNull(result);
        Assert.Equal("Serenity", result.Name);
    }

    [Fact]
    public async Task GetByIdAsync_MissingId_ReturnsNull()
    {
        var result = await _repository.GetByIdAsync(9999);

        Assert.Null(result);
    }

    [Fact]
    public async Task SearchAsync_MatchesName_Email_OrNotes()
    {
        // "match" appears verbatim (same case) in the name, email, and notes of
        // three groups, so the assertion holds whether the provider treats the
        // Contains translation as case-sensitive or not.
        await SeedAsync(
            new Group { Id = 1, Name = "Aaa match" },
            new Group { Id = 2, Name = "Bbb", Email = "hello@match.org" },
            new Group { Id = 3, Name = "Ccc", Notes = "a match is here" },
            new Group { Id = 4, Name = "Ddd unrelated" });

        var result = await _repository.SearchAsync("match");

        Assert.Equal(new[] { "Aaa match", "Bbb", "Ccc" }, result.Select(g => g.Name));
    }

    [Fact]
    public async Task GetByDistrictAsync_ReturnsOnlyGroupsInDistrict()
    {
        await SeedAsync(
            new Group { Id = 1, Name = "One", DistrictId = 1 },
            new Group { Id = 2, Name = "Two", DistrictId = 2 },
            new Group { Id = 3, Name = "Three", DistrictId = 1 });

        var result = await _repository.GetByDistrictAsync(1);

        Assert.Equal(new[] { "One", "Three" }, result.Select(g => g.Name));
    }

    private async Task SeedAsync(params Group[] groups)
    {
        await using var ctx = new UnityDbContext(_options);
        ctx.Groups.AddRange(groups);
        await ctx.SaveChangesAsync();
    }

    // Minimal IDbContextFactory over shared options; CreateDbContextAsync uses
    // the interface's default implementation, which is all the repository needs.
    private sealed class TestDbContextFactory(DbContextOptions<UnityDbContext> options)
        : IDbContextFactory<UnityDbContext>
    {
        public UnityDbContext CreateDbContext() => new(options);
    }
}
