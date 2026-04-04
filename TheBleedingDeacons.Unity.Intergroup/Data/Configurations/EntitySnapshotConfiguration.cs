using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Unity.Intergroup.Data.Configurations;

public class EntitySnapshotConfiguration : IEntityTypeConfiguration<EntitySnapshot>
{
    public void Configure(EntityTypeBuilder<EntitySnapshot> builder)
    {
        builder.ToTable("EntitySnapshots");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .ValueGeneratedOnAdd();

        builder.Property(s => s.EntityType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.JsonData)
            .IsRequired();

        builder.Property(s => s.SnapshotUtc)
            .HasPrecision(3);

        // Composite index for fast lookup during reconciliation
        builder.HasIndex(s => new { s.EntityType, s.EntityKey });
    }
}