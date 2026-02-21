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

        builder.Property(e => e.Name)
            .HasColumnName("Gsr Name")
            .HasMaxLength(255);

        builder.Property(e => e.EmailPersonal)
            .HasColumnName("Gsr Email Personal")
            .HasMaxLength(255);

        builder.Property(e => e.Phone)
            .HasColumnName("Gsr Phone")
            .HasMaxLength(60);

        // One-to-one: Member belongs to one Group
        builder.HasOne(e => e.Group)
            .WithOne(g => g.Gsr)
            .HasForeignKey<Member>(e => e.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
