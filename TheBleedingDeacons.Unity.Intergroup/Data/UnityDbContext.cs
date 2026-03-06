using Microsoft.EntityFrameworkCore;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using Contact = TheBleedingDeacons.Unity.Intergroup.Entities.Contact;

namespace TheBleedingDeacons.Unity.Intergroup.Data;

public class UnityDbContext : DbContext
{
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Meeting> Meetings => Set<Meeting>();
    public DbSet<Contact> Contacts => Set<Contact>();
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

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampUpdated();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampUpdated();
        return base.SaveChanges();
    }

    /// <summary>
    /// Sets the <c>Updated</c> property to <see cref="DateTime.UtcNow"/> on every
    /// tracked entity that has been added or modified and exposes an <c>Updated</c> property.
    /// </summary>
    private void StampUpdated()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            var updatedProp = entry.Properties
                .FirstOrDefault(p => p.Metadata.Name == nameof(Entities.Group.Updated));

            if (updatedProp is not null)
                updatedProp.CurrentValue = now;
        }
    }
}
