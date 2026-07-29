using Hiram.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hiram.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Type).HasColumnName("type").HasMaxLength(128).IsRequired();
        builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.ProcessedAtUtc).HasColumnName("processed_at_utc");
        builder.Property(x => x.TraceParent).HasColumnName("trace_parent").HasMaxLength(64);
        builder.Property(x => x.DispatchAt).HasColumnName("dispatch_at");
        builder.Property(x => x.AvailableAt).HasColumnName("available_at").IsRequired();
        builder.Property(x => x.LeaseUntil).HasColumnName("lease_until");
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(4000);

        // Partial index over the queue hot path: only unprocessed rows, oldest first.
        builder.HasIndex(x => x.CreatedAtUtc)
            .HasDatabaseName("ix_outbox_messages_unprocessed")
            .HasFilter("processed_at_utc IS NULL");

        builder.HasIndex(x => new { x.AvailableAt, x.CreatedAtUtc })
            .HasDatabaseName("ix_outbox_messages_available")
            .HasFilter("processed_at_utc IS NULL");
    }
}
