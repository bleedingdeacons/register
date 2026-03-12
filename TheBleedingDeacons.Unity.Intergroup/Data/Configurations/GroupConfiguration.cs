using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Unity.Intergroup.Data.Configurations;

public class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("Groups");

        builder.HasKey(g => g.Id);

        // Unity provides the ID — don't let the DB auto-generate
        builder.Property(g => g.Id)
            .ValueGeneratedNever();

        builder.Property(g => g.Name)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(g => g.Email)
            .HasMaxLength(255);

        builder.Property(g => g.Phone)
            .HasMaxLength(60);

        builder.Property(g => g.Website)
            .HasMaxLength(500);

        builder.Property(g => g.Notes)
            .HasMaxLength(2000);

        builder.Property(g => g.Registered)
            .HasDefaultValue(false);

        builder.Property(g => g.Updated)
            .HasPrecision(3);

        // One-to-many: Group → Members (GSRs whose home group this is)
        builder.HasMany(g => g.Members)
            .WithOne(m => m.HomeGroup)
            .HasForeignKey(m => m.HomeGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        // One-to-many: Group → Contacts
        builder.HasMany(g => g.Contacts)
            .WithOne(c => c.Group)
            .HasForeignKey(c => c.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
