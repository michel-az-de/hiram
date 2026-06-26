using Hiram.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hiram.Infrastructure.Persistence.Configurations;

internal sealed class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.ToTable("webhook_endpoints");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Url).HasColumnName("url").HasMaxLength(2048).IsRequired();
        builder.Property(x => x.SecretProtected).HasColumnName("secret_protected").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_webhook_endpoints_tenant_id");
        builder.HasIndex(x => new { x.TenantId, x.Url })
            .HasDatabaseName("ux_webhook_endpoints_tenant_url")
            .IsUnique();
    }
}
