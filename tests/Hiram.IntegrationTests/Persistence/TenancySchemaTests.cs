using Hiram.Domain.Notifications;
using Hiram.Domain.Tenants;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Persistence;

public class TenancySchemaTests : IAsyncLifetime
{
    private static readonly Guid DevTenant = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private HiramDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<HiramDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new HiramDbContext(options);
    }

    private static NotificationRequest NewRequest(Guid tenantId, string? idempotencyKey) =>
        new(Guid.NewGuid(), tenantId, NotificationChannel.Email, "ops@example.com", "hello", "f1", DateTimeOffset.UtcNow, idempotencyKey);

    [Fact]
    public async Task Migration_SeedsDevTenant_InLiveDeliveryMode()
    {
        await using var context = NewContext();

        var tenant = await context.Tenants.FindAsync(DevTenant);

        Assert.NotNull(tenant);
        Assert.Equal(DeliveryMode.Live, tenant!.DeliveryMode);
    }

    [Fact]
    public async Task IdempotencyIndex_RejectsDuplicateKeyWithinTenant()
    {
        await using (var seed = NewContext())
        {
            seed.NotificationRequests.Add(NewRequest(DevTenant, "evt-1"));
            await seed.SaveChangesAsync();
        }

        await using var context = NewContext();
        context.NotificationRequests.Add(NewRequest(DevTenant, "evt-1"));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task IdempotencyIndex_AllowsManyRequestsWithoutKey()
    {
        await using var context = NewContext();
        context.NotificationRequests.Add(NewRequest(DevTenant, idempotencyKey: null));
        context.NotificationRequests.Add(NewRequest(DevTenant, idempotencyKey: null));

        var saved = await context.SaveChangesAsync();

        Assert.Equal(2, saved);
    }

    [Fact]
    public async Task IdempotencyIndex_ScopesKeysPerTenant()
    {
        await using var context = NewContext();
        context.NotificationRequests.Add(NewRequest(DevTenant, "evt-9"));
        context.NotificationRequests.Add(NewRequest(Guid.NewGuid(), "evt-9"));

        var saved = await context.SaveChangesAsync();

        Assert.Equal(2, saved);
    }
}
