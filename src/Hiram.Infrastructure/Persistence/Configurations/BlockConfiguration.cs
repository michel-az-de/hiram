using Hiram.Domain.Blocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hiram.Infrastructure.Persistence.Configurations;

internal sealed class BlockConfiguration : IEntityTypeConfiguration<Block>
{
    public void Configure(EntityTypeBuilder<Block> builder)
    {
        builder.ToTable("blocks");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.Channel).HasColumnName("channel").HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
        builder.Property(x => x.ActivatedAtUtc).HasColumnName("activated_at_utc").IsRequired();
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
        builder.Property(x => x.RemovedAtUtc).HasColumnName("removed_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.RemovedAtUtc })
            .HasDatabaseName("ix_blocks_tenant_removed");
    }
}
