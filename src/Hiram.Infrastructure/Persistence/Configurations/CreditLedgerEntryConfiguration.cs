using Hiram.Domain.Metering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hiram.Infrastructure.Persistence.Configurations;

internal sealed class CreditLedgerEntryConfiguration : IEntityTypeConfiguration<CreditLedgerEntry>
{
    public void Configure(EntityTypeBuilder<CreditLedgerEntry> builder)
    {
        builder.ToTable("credit_ledger");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.NotificationId).HasColumnName("notification_id");
        builder.Property(x => x.Amount).HasColumnName("amount").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(128).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_credit_ledger_tenant_id");
        builder.HasIndex(x => x.NotificationId).HasDatabaseName("ix_credit_ledger_notification_id");
    }
}
