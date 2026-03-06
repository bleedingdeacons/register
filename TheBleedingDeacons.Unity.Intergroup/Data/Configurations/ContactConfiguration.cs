using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using Contact = TheBleedingDeacons.Unity.Intergroup.Entities.Contact;

namespace TheBleedingDeacons.Unity.Intergroup.Data.Configurations;

public class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("Contacts");

        builder.HasKey(c => c.Id);

        // Contacts are locally generated during sync — use auto-increment
        builder.Property(c => c.Id)
            .ValueGeneratedOnAdd();

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(c => c.Email)
            .HasMaxLength(255);

        builder.Property(c => c.Phone)
            .HasMaxLength(60);

        builder.HasIndex(c => c.GroupId);
    }
}
