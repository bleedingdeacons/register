using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TheBleedingDeacons.Intergroup.Data.Models;

namespace TheBleedingDeacons.Intergroup.Data.Configurations;

public class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.ToTable("Positions");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .ValueGeneratedNever();

        builder.Property(p => p.ShortDescription)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.LongName)
            .HasMaxLength(255);

        builder.Property(p => p.Email)
            .HasMaxLength(255);

        // Holder FK is now on Member.IntergroupPositionId (configured in MemberConfiguration)
    }
}
