using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Data.Configurations;

public class QueuedApiCallConfiguration : IEntityTypeConfiguration<QueuedApiCall>
{
    public void Configure(EntityTypeBuilder<QueuedApiCall> builder)
    {
        builder.HasKey(q => q.Id);

        builder.Property(q => q.OperationType)
               .IsRequired()
               .HasMaxLength(100);

        builder.Property(q => q.Url)
               .IsRequired()
               .HasMaxLength(2000);

        builder.Property(q => q.HttpMethod)
               .IsRequired()
               .HasMaxLength(10);

        builder.Property(q => q.JsonPayload)
               .HasMaxLength(8000);

        builder.Property(q => q.LastError)
               .HasMaxLength(1000);

        builder.HasIndex(q => new { q.IsFailed, q.CreatedUtc });
    }
}
