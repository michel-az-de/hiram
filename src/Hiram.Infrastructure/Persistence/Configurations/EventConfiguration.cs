using Hiram.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hiram.Infrastructure.Persistence.Configurations;

internal sealed class EventConfiguration : IEntityTypeConfiguration<NotificationEvent>
{
    public void Configure(EntityTypeBuilder<NotificationEvent> builder)
    {
        builder.ToTable("events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(120).IsRequired();
        builder.Property(x => x.EventId).HasColumnName("event_id").HasMaxLength(255).IsRequired();
        builder.Property(x => x.EmissionSeq).HasColumnName("emission_seq").IsRequired();
        builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        // One accepted event per (tenant, event id). This is the durable arbiter of event idempotency.
        builder.HasIndex(x => new { x.TenantId, x.EventId })
            .HasDatabaseName("ux_events_tenant_event_id")
            .IsUnique();
    }
}
