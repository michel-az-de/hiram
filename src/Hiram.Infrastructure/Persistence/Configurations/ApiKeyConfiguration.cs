using Hiram.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hiram.Infrastructure.Persistence.Configurations;

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.KeyHash).HasColumnName("key_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.KeyPrefix).HasColumnName("key_prefix").HasMaxLength(16).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.RevokedAtUtc).HasColumnName("revoked_at_utc");
        builder.Property(x => x.LastUsedAtUtc).HasColumnName("last_used_at_utc");

        // Authentication resolves a key by the hash of the presented secret, so the lookup column is unique.
        builder.HasIndex(x => x.KeyHash)
            .HasDatabaseName("ux_api_keys_key_hash")
            .IsUnique();

        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_api_keys_tenant_id");
    }
}
