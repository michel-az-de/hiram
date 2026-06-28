using Hiram.Domain.Notifications;
using Hiram.Domain.Routines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hiram.Infrastructure.Persistence.Configurations;

internal sealed class RoutineConfiguration : IEntityTypeConfiguration<Routine>
{
    public void Configure(EntityTypeBuilder<Routine> builder)
    {
        builder.ToTable("routines");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(120).IsRequired();
        builder.Property(x => x.TemplateName).HasColumnName("template_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Active).HasColumnName("active").IsRequired();

        var channelsComparer = new ValueComparer<IReadOnlyList<NotificationChannel>>(
            (a, b) => a!.SequenceEqual(b!),
            v => v.Aggregate(0, (hash, channel) => HashCode.Combine(hash, channel.GetHashCode())),
            v => v.ToList());

        builder.Property(x => x.Channels)
            .HasColumnName("channels")
            .HasColumnType("text")
            .HasConversion(
                v => string.Join(',', v.Select(c => c.ToString())),
                s => s.Length == 0
                    ? new List<NotificationChannel>()
                    : s.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<NotificationChannel>).ToList(),
                channelsComparer)
            .IsRequired();

        // The engine matches active routines by (tenant, event type); tenant null means a global routine.
        builder.HasIndex(x => new { x.TenantId, x.EventType, x.Active })
            .HasDatabaseName("ix_routines_tenant_event_active");
    }
}
