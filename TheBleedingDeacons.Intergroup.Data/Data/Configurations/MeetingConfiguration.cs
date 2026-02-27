using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TheBleedingDeacons.Intergroup.Data.Models;

namespace TheBleedingDeacons.Intergroup.Data.Configurations;

public class MeetingConfiguration : IEntityTypeConfiguration<Meeting>
{
    public void Configure(EntityTypeBuilder<Meeting> builder)
    {
        builder.ToTable("Meetings");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .ValueGeneratedNever();

        builder.Property(m => m.Name)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(m => m.DayOfWeek)
            .HasMaxLength(10);

        builder.Property(m => m.Time)
            .HasMaxLength(10);

        builder.Property(m => m.EndTime)
            .HasMaxLength(10);

        builder.Property(m => m.LocationName)
            .HasMaxLength(255);

        builder.Property(m => m.Address)
            .HasMaxLength(500);

        builder.Property(m => m.OnlineLink)
            .HasMaxLength(500);

        builder.Property(m => m.Types)
            .HasMaxLength(500);

        // Many-to-one: Meeting → Group
        builder.HasOne(m => m.Group)
            .WithMany(g => g.Meetings)
            .HasForeignKey(m => m.GroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(m => m.GroupId);
        builder.HasIndex(m => m.Day);
    }
}
