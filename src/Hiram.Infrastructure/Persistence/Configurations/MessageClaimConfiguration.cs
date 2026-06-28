using Hiram.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hiram.Infrastructure.Persistence.Configurations;

internal sealed class MessageClaimConfiguration : IEntityTypeConfiguration<MessageClaim>
{
    public void Configure(EntityTypeBuilder<MessageClaim> builder)
    {
        builder.ToTable("message_claims");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.MessageKey).HasColumnName("message_key").HasMaxLength(128).IsRequired();
        builder.Property(x => x.ClaimedAtUtc).HasColumnName("claimed_at_utc").IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.MessageKey })
            .HasDatabaseName("ux_message_claims_tenant_key")
            .IsUnique();
    }
}
