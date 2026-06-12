using Hiram.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hiram.Infrastructure.Persistence.Configurations;

internal sealed class TenantProviderConfigConfiguration : IEntityTypeConfiguration<TenantProviderConfig>
{
    public void Configure(EntityTypeBuilder<TenantProviderConfig> builder)
    {
        builder.ToTable("tenant_provider_configs");

        // A tenant configures one provider per channel, so the channel completes the key.
        builder.HasKey(x => new { x.TenantId, x.Channel });
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.Channel).HasColumnName("channel").HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Settings).HasColumnName("settings").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.SecretProtected).HasColumnName("secret_protected");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
    }
}
