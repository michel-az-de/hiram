using Hiram.Domain.DeadLetters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hiram.Infrastructure.Persistence.Configurations;

internal sealed class DeadLetterMessageConfiguration : IEntityTypeConfiguration<DeadLetterMessage>
{
    public void Configure(EntityTypeBuilder<DeadLetterMessage> builder)
    {
        builder.ToTable("dead_letter_messages");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.NotificationId).HasColumnName("notification_id").IsRequired();
        builder.Property(x => x.Channel).HasColumnName("channel").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(256).IsRequired();
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.ReplayedAtUtc).HasColumnName("replayed_at_utc");

        builder.HasIndex(x => x.TenantId, "ix_dead_letter_messages_tenant_id");
        builder.HasIndex(x => x.NotificationId, "ix_dead_letter_messages_notification_id");

        // At most one open dead letter per notification, so replay always targets an unambiguous row.
        builder.HasIndex(x => x.NotificationId, "ux_dead_letter_messages_open")
            .IsUnique()
            .HasFilter("replayed_at_utc IS NULL");
    }
}
