using Microsoft.EntityFrameworkCore;
using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Data;

public class RegisterContext : DbContext
{
    public DbSet<Meeting> Meetings { get; set; }
    public DbSet<Group> Groups { get; set; }
    public DbSet<Member> Members { get; set; }
    public DbSet<Position> Positions { get; set; }
    public DbSet<IntergroupMeeting> IntergroupMeetings { get; set; }

    /// <summary>Outbox queue for API calls that failed or were made while offline.</summary>
    public DbSet<QueuedApiCall> QueuedApiCalls { get; set; }

    public RegisterContext(DbContextOptions<RegisterContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
#if DEBUG
        optionsBuilder.EnableSensitiveDataLogging();
#endif
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Apply all IEntityTypeConfiguration implementations from the current assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RegisterContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}