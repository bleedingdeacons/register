using Microsoft.EntityFrameworkCore;
using TheBleedingDeacons.Intergroup.Data.Models;

namespace TheBleedingDeacons.Intergroup.Data;

public class UnityDbContext : DbContext
{
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Meeting> Meetings => Set<Meeting>();
    public DbSet<IntergroupMeeting> IntergroupMeetings => Set<IntergroupMeeting>();

    public UnityDbContext(DbContextOptions<UnityDbContext> options) : base(options) { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
#if DEBUG
        optionsBuilder.EnableSensitiveDataLogging();
#endif
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UnityDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
