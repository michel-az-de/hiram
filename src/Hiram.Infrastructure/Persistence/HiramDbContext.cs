using Hiram.Domain.Notifications;
using Hiram.Domain.Outbox;
using Hiram.Domain.Tenants;
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
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<TenantProviderConfig> TenantProviderConfigs => Set<TenantProviderConfig>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HiramDbContext).Assembly);
    }
}
