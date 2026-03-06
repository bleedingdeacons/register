using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Unity.Intergroup.Data.Configurations;

public class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.ToTable("Members");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .ValueGeneratedNever();

        builder.Property(m => m.AnonymousName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(m => m.PrivateName)
            .HasMaxLength(255);

        builder.Property(m => m.Email)
            .HasMaxLength(255);

        builder.Property(m => m.PersonalEmail)
            .HasMaxLength(255);

        builder.Property(m => m.MobileNumber)
            .HasMaxLength(60);

        // HomeGroup FK is configured from GroupConfiguration (WithOne → HasForeignKey)

        // Many-to-one: multiple members can hold the same position
        builder.HasOne(m => m.IntergroupPosition)
            .WithMany(p => p.Holders)
            .HasForeignKey(m => m.IntergroupPositionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(m => m.HomeGroupId);
        builder.HasIndex(m => m.IntergroupPositionId);
        builder.HasIndex(m => m.IsGsr);
    }
}
