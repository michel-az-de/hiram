using Hiram.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hiram.Infrastructure.Persistence.Configurations;

internal sealed class DeliveryAttemptConfiguration : IEntityTypeConfiguration<DeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<DeliveryAttempt> builder)
    {
        builder.ToTable("delivery_attempts");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.NotificationId).HasColumnName("notification_id").IsRequired();
        builder.Property(x => x.AttemptNumber).HasColumnName("attempt_number").IsRequired();
        builder.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Error).HasColumnName("error");
        builder.Property(x => x.Duration).HasColumnName("duration").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.Shadowed).HasColumnName("shadowed").IsRequired();
        builder.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64);
        builder.Property(x => x.ProviderMessageId).HasColumnName("provider_message_id").HasMaxLength(128);
        builder.Property(x => x.TrialContent).HasColumnName("trial_content").HasDefaultValue(false).IsRequired();

        builder.HasIndex(x => x.NotificationId).HasDatabaseName("ix_delivery_attempts_notification_id");
        builder.HasIndex(x => x.ProviderMessageId).HasDatabaseName("ix_delivery_attempts_provider_message_id");
    }
}
