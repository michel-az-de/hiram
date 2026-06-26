using Hiram.Domain.Push;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hiram.Infrastructure.Persistence.Configurations;

internal sealed class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.ToTable("push_subscriptions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Endpoint).HasColumnName("endpoint").HasMaxLength(2048).IsRequired();
        builder.Property(x => x.P256dh).HasColumnName("p256dh").HasMaxLength(256).IsRequired();
        builder.Property(x => x.Auth).HasColumnName("auth").HasMaxLength(256).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_push_subscriptions_tenant_id");
        builder.HasIndex(x => new { x.TenantId, x.Endpoint })
            .HasDatabaseName("ux_push_subscriptions_tenant_endpoint")
            .IsUnique();
    }
}
