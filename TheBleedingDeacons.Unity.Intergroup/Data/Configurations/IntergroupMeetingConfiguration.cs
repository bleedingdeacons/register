using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Unity.Intergroup.Data.Configurations;

public class IntergroupMeetingConfiguration : IEntityTypeConfiguration<IntergroupMeeting>
{
    public void Configure(EntityTypeBuilder<IntergroupMeeting> builder)
    {
        builder.ToTable("IntergroupMeetings");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .ValueGeneratedNever();

        builder.Property(m => m.Title)
            .HasMaxLength(255);

        builder.Property(m => m.Date)
            .HasMaxLength(10);

        builder.Property(m => m.GroupAttendeeIds)
            .HasMaxLength(1000);

        builder.Property(m => m.GroupAttendeeNames)
            .HasMaxLength(2000);

        builder.Property(m => m.OfficerAttendeeIds)
            .HasMaxLength(1000);

        builder.Property(m => m.OfficerAttendeeNames)
            .HasMaxLength(2000);

        builder.Property(m => m.Updated);
    }
}
