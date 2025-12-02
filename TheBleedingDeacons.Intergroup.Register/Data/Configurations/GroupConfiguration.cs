using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheBleedingDeacons.Intergroup.Register.Data.Configurations
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using TheBleedingDeacons.Intergroup.Register.Models;

    namespace TheBleedingDeacons.Intergroup.Register.Data.Configurations;

    public class GroupConfiguration : IEntityTypeConfiguration<Group>
    {
        public void Configure(EntityTypeBuilder<Group> builder)
        {
            builder.ToTable("Groups");

            builder.HasKey(e => e.ID);

            builder.Property(e => e.Time)
                .HasMaxLength(7);

            builder.Property(e => e.EndTime)
                .HasMaxLength(7);

            builder.Property(e => e.Day)
                .HasMaxLength(50);

            builder.Property(e => e.Name)
                .HasMaxLength(255);

            builder.Property(e => e.GsrName)
                .HasColumnName("Gsr Name")
                .HasMaxLength(255);

            builder.Property(e => e.GsrEmailPersonal)
                .HasColumnName("Gsr Email Personal")
                .HasMaxLength(255);

            builder.Property(e => e.GsrPhone)
                .HasColumnName("Gsr Phone")
                .HasMaxLength(60);

            builder.Property(e => e.GroupGenericEmail)
                .HasColumnName("Group Generic Email")
                .HasMaxLength(255);

            builder.Property(e => e.UsingGeneric)
                .HasColumnName("Using Generic")
                .HasColumnType("bit");

            builder.Property(e => e.Location)
                .HasColumnName("Location")
                .HasMaxLength(100);

            builder.Property(e => e.Address)
                .HasColumnName("Address")
                .HasMaxLength(255);

            builder.Property(e => e.Contact1Name)
                .HasColumnName("Contact 1 Name")
                .HasMaxLength(255);

            builder.Property(e => e.Contact1Email)
                .HasColumnName("Contact 1 Email")
                .HasMaxLength(255);

            builder.Property(e => e.Contact1Phone)
                .HasColumnName("Contact 1 Phone")
                .HasMaxLength(20);

            builder.Property(e => e.Contact2Name)
                .HasColumnName("Contact 2 Name")
                .HasMaxLength(255);

            builder.Property(e => e.Contact2Email)
                .HasColumnName("Contact 2 Email")
                .HasMaxLength(255);

            builder.Property(e => e.Contact2Phone)
                .HasColumnName("Contact 2 Phone")
                .HasMaxLength(20);

            builder.Property(e => e.Contact3Name)
                .HasColumnName("Contact 3 Name")
                .HasMaxLength(255);

            builder.Property(e => e.Contact3Email)
                .HasColumnName("Contact 3 Email")
                .HasMaxLength(255);

            builder.Property(e => e.Contact3Phone)
                .HasColumnName("Contact 3 Phone")
                .HasMaxLength(20);

            builder.Property(e => e.Types)
                .HasColumnName("Types")
                .HasMaxLength(255);

            builder.Property(e => e.Updated)
                .HasColumnName("Updated")
                .HasColumnType("DateTime");

            builder.Property(e => e.Attended)
                .HasColumnName("Attended")
                .HasColumnType("bit");
        }
    }
    internal class GroupConfiguration
    {
    }
}
