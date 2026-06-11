using Hiram.Domain.Notifications;
using Hiram.Domain.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Hiram.Infrastructure.Persistence;

public sealed class HiramDbContext : DbContext
{
    public const string Schema = "notifications";

    public HiramDbContext(DbContextOptions<HiramDbContext> options) : base(options)
    {
    }

    public DbSet<NotificationRequest> NotificationRequests => Set<NotificationRequest>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HiramDbContext).Assembly);
    }
}
