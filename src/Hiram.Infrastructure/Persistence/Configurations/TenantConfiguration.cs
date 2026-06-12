using Hiram.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hiram.Infrastructure.Persistence.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    // The fixed development tenant inherited from F0, promoted here to a real row in live delivery mode.
    public static readonly Guid DevTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.DeliveryMode).HasColumnName("delivery_mode").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        builder.HasData(new
        {
            Id = DevTenantId,
            Name = "dev",
            DeliveryMode = DeliveryMode.Live,
            CreatedAtUtc = new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero)
        });
    }
}
