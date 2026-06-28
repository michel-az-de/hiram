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

        // Partial index over the relay hot path: only unprocessed rows, oldest first.
        builder.HasIndex(x => x.CreatedAtUtc)
            .HasDatabaseName("ix_outbox_messages_unprocessed")
            .HasFilter("processed_at_utc IS NULL");
    }
}
