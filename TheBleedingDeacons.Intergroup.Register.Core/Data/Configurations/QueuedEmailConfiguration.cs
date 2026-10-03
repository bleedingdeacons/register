using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Data.Configurations
{
    /// <summary>
    /// EF Core fluent configuration for the <see cref="QueuedEmail"/> entity.
    ///
    /// <para>Lifted out of <see cref="MailDbContext.OnModelCreating"/> so that
    /// per-entity mapping concerns (keys, value conversions, indexes) live
    /// next to the entity they describe rather than piling up inside the
    /// context. The context now picks this up automatically via
    /// <c>ApplyConfigurationsFromAssembly</c>; new <see cref="QueuedEmail"/>
    /// mappings should be added here, not inlined back into the context.</para>
    ///
    /// <para>Schema-affecting changes here will <b>only</b> apply on a fresh
    /// database, because the project uses
    /// <c>Database.EnsureCreated()</c> rather than a migrations pipeline.
    /// Existing installs keep whatever schema they were first created with.</para>
    /// </summary>
    public sealed class QueuedEmailConfiguration : IEntityTypeConfiguration<QueuedEmail>
    {
        public void Configure(EntityTypeBuilder<QueuedEmail> entity)
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            // Status is an enum; persist as int so reads survive future
            // value reorderings if any new status is appended (the int
            // values on EmailStatus are explicit, so the mapping is stable).
            entity.Property(e => e.Status)
                .HasConversion<int>();

            // Optional Reply-To override. Nullable, capped at 200 chars
            // to match the From column. NULL when the queueing caller
            // didn't specify a reply-to (the common case for welcome
            // emails); populated when callers like ComplianceService
            // need replies routed somewhere other than the From address.
            entity.Property(e => e.ReplyTo)
                .HasMaxLength(200)
                .IsRequired(false);

            // Hot-path indexes. ProcessQueueAsync filters by Status and
            // orders by CreatedAt, so both columns earn their own index
            // rather than relying on a composite — keeps the indexes
            // useful for ad-hoc queries from the diagnostics page too.
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
        }
    }
}
