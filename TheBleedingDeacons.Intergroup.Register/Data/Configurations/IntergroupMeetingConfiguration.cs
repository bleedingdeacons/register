using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Data.Configurations;

public class IntergroupMeetingConfiguration : IEntityTypeConfiguration<IntergroupMeeting>
{
    public void Configure(EntityTypeBuilder<IntergroupMeeting> builder)
    {
        builder.ToTable("IntergroupMeetings");

        builder.HasKey(e => e.ID);

        builder.Property(e => e.Title)
            .HasMaxLength(255);

        builder.Property(e => e.Date)
            .HasMaxLength(10); // yyyy-MM-dd

        builder.Property(e => e.GroupAttendeeIds)
            .HasColumnName("Group Attendee Ids")
            .HasMaxLength(1000);

        builder.Property(e => e.GroupAttendeeNames)
            .HasColumnName("Group Attendee Names")
            .HasMaxLength(2000);

        builder.Property(e => e.OfficerAttendeeIds)
            .HasColumnName("Officer Attendee Ids")
            .HasMaxLength(1000);

        builder.Property(e => e.OfficerAttendeeNames)
            .HasColumnName("Officer Attendee Names")
            .HasMaxLength(2000);
    }
}
