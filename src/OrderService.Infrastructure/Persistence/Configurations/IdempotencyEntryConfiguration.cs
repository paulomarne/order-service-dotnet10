using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderService.Infrastructure.Persistence.Idempotency;

namespace OrderService.Infrastructure.Persistence.Configurations;

public sealed class IdempotencyEntryConfiguration : IEntityTypeConfiguration<IdempotencyEntry>
{
    public void Configure(EntityTypeBuilder<IdempotencyEntry> builder)
    {
        builder.ToTable("idempotency_entries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Key).HasMaxLength(128).IsRequired();
        builder.HasIndex(entry => entry.Key).IsUnique();
        builder.Property(entry => entry.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(entry => entry.State).HasMaxLength(16).IsRequired();
        builder.Property(entry => entry.ContentType).HasMaxLength(128);
        builder.Property(entry => entry.Location).HasMaxLength(512);
        builder.Property(entry => entry.CreatedAt).IsRequired();
    }
}
