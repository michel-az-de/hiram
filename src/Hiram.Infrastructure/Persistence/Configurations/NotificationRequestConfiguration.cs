using Hiram.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hiram.Infrastructure.Persistence.Configurations;

internal sealed class NotificationRequestConfiguration : IEntityTypeConfiguration<NotificationRequest>
{
    public void Configure(EntityTypeBuilder<NotificationRequest> builder)
    {
        builder.ToTable("notification_requests");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Channel).HasColumnName("channel").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Recipient).HasColumnName("recipient").HasMaxLength(320).IsRequired();
        builder.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(512);
        builder.Property(x => x.Body).HasColumnName("body").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(255);

        // One accepted notification per (tenant, idempotency key). Rows without a key are unconstrained,
        // so the unique index is partial. PostgreSQL is the authority for idempotency.
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
            .HasDatabaseName("ux_notification_requests_idempotency")
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL");
    }
}
