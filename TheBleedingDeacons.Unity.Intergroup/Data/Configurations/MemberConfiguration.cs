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

        builder.Property(m => m.IntergroupPositionRotation)
            .HasMaxLength(20);

        builder.Property(m => m.Updated)
            .HasPrecision(3);

        // GDPR compliance fields. Lengths chosen to match the Unity
        // server's validation (version/method ≤ 50, statement ≤ 50 000).
        // GdprAcceptedAt uses the same precision as Updated so timestamp
        // comparisons in the snapshot diff round-trip cleanly.
        builder.Property(m => m.GdprAcceptedAt)
            .HasPrecision(3);

        builder.Property(m => m.GdprAcceptanceVersion)
            .HasMaxLength(50);

        builder.Property(m => m.GdprAcceptanceMethod)
            .HasMaxLength(50);

        builder.Property(m => m.GdprAcceptanceStatement)
            .HasMaxLength(50000);

        // PolicyId is the WordPress post ID of the accepted privacy
        // policy — a plain int, no length to configure. Defined here
        // for completeness so adding the column is symmetrical with
        // the other GDPR fields and shows up in EF model snapshots.
        builder.Property(m => m.GdprAcceptancePolicyId);

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