using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Data.Configurations;

public class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.ToTable("Members");

        builder.HasKey(e => e.ID);

        // SQLite inserts the supplied ID when non-zero (Unity sync path),
        // and auto-assigns a ROWID when ID == 0 (locally created GSR, not yet synced).

        builder.Property(e => e.Name)
            .HasColumnName("Gsr Name")
            .HasMaxLength(255);

        builder.Property(e => e.EmailPersonal)
            .HasColumnName("Gsr Email Personal")
            .HasMaxLength(255);

        builder.Property(e => e.Phone)
            .HasColumnName("Gsr Phone")
            .HasMaxLength(60);

        // One-to-many: Member belongs to one Group as a GSR; a Group can have multiple GSRs
        builder.HasOne(e => e.Group)
            .WithMany(g => g.Gsrs)
            .HasForeignKey(e => e.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}