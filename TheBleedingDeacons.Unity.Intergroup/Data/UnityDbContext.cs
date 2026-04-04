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
    public DbSet<EntitySnapshot> EntitySnapshots => Set<EntitySnapshot>();

    /// <summary>
    /// When <c>true</c>, <see cref="SaveChangesAsync"/> and <see cref="SaveChanges"/>
    /// will not automatically set the <c>Updated</c> timestamp on tracked entities.
    /// Use this during bulk-sync operations where the data comes straight from Unity
    /// and no local change tracking is desired.
    /// </summary>
    public bool SuppressUpdatedStamp { get; set; }

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

    /// <summary>
    /// Deletes all rows from every table and clears the change tracker.
    /// Tables are deleted dependents-first to respect foreign-key constraints.
    /// </summary>
    public async Task PurgeDatabaseAsync(CancellationToken ct = default)
    {
        await Meetings.ExecuteDeleteAsync(ct);
        await Contacts.ExecuteDeleteAsync(ct);
        await IntergroupMeetings.ExecuteDeleteAsync(ct);
        await Positions.ExecuteDeleteAsync(ct);
        await Members.ExecuteDeleteAsync(ct);
        await Groups.ExecuteDeleteAsync(ct);
        await EntitySnapshots.ExecuteDeleteAsync(ct);

        ChangeTracker.Clear();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (!SuppressUpdatedStamp) StampUpdated();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        if (!SuppressUpdatedStamp) StampUpdated();
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