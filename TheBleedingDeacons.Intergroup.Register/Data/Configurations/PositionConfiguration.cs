using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Data.Configurations;

public class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.ToTable("Positions");

        builder.HasKey(e => e.ID);

        builder.Property(e => e.PositionName)
            .HasColumnName("Position Name")
            .HasMaxLength(100);

        builder.Property(e => e.PositionLongName)
            .HasColumnName("Position Long Name")
            .HasMaxLength(255);

        builder.Property(e => e.PositionGenericEmail)
            .HasColumnName("Position Generic Email")
            .HasMaxLength(255);

        builder.Property(e => e.MemberAnonymousName)
            .HasColumnName("Member Anonymous Name")
            .HasMaxLength(255);

        builder.Property(e => e.MemberPersonalEmail)
            .HasColumnName("Member Personal Email")
            .HasMaxLength(255);

        builder.Property(e => e.MemberMobile)
            .HasColumnName("Member Mobile")
            .HasMaxLength(20);

        builder.Property(e => e.PositionDuration)
            .HasColumnName("Position Duration")
            .HasMaxLength(50);

        builder.Property(e => e.StartedService)
            .HasColumnName("Started Service");

        builder.Property(e => e.Updated)
            .HasColumnName("Updated")
            .HasColumnType("DateTime");

        builder.Property(e => e.Attended)
            .HasColumnName("Attended")
            .HasColumnType("bit");
    }
}