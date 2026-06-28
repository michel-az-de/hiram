using Hiram.Domain.Consents;
using Hiram.Domain.DeadLetters;
using Hiram.Domain.Events;
using Hiram.Domain.Metering;
using Hiram.Domain.Notifications;
using Hiram.Domain.Outbox;
using Hiram.Domain.Push;
using Hiram.Domain.Routines;
using Hiram.Domain.Templates;
using Hiram.Domain.Tenants;
using Hiram.Domain.Webhooks;
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
    public DbSet<NotificationEvent> Events => Set<NotificationEvent>();
    public DbSet<Routine> Routines => Set<Routine>();
    public DbSet<Consent> Consents => Set<Consent>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<TenantProviderConfig> TenantProviderConfigs => Set<TenantProviderConfig>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();
    public DbSet<DeadLetterMessage> DeadLetterMessages => Set<DeadLetterMessage>();
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();
    public DbSet<CreditLedgerEntry> CreditLedger => Set<CreditLedgerEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HiramDbContext).Assembly);
    }
}
