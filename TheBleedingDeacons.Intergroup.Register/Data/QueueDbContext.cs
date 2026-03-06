using Microsoft.EntityFrameworkCore;
using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Data;

/// <summary>
/// Lightweight DbContext that only manages the offline API-call outbox.
/// Domain entities are now managed by <see cref="TheBleedingDeacons.Unity.Intergroup.Data.UnityDbContext"/>.
/// </summary>
public class QueueDbContext : DbContext
{
    public DbSet<QueuedApiCall> QueuedApiCalls { get; set; }

    public QueueDbContext(DbContextOptions<QueueDbContext> options) : base(options) { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
#if DEBUG
        optionsBuilder.EnableSensitiveDataLogging();
#endif
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(QueueDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
