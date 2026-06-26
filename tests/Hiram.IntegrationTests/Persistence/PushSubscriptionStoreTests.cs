using Hiram.Application.Push;
using Hiram.Domain.Push;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Persistence;

public class PushSubscriptionStoreTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private HiramDbContext NewContext() =>
        new(new DbContextOptionsBuilder<HiramDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private static PushSubscription NewSubscription(Guid tenantId, string endpoint) =>
        new(Guid.NewGuid(), tenantId, endpoint, "p256dh-key", "auth-secret", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Add_And_Get_RoundTrip()
    {
        var tenantId = Guid.NewGuid();
        var subscription = NewSubscription(tenantId, "https://push.example.com/a");
        await using (var context = NewContext())
            await new PushSubscriptionStore(context).AddAsync(subscription, CancellationToken.None);

        await using var read = NewContext();
        var found = await new PushSubscriptionStore(read).GetAsync(tenantId, subscription.Id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("https://push.example.com/a", found!.Endpoint);
    }

    [Fact]
    public async Task Add_Throws_OnDuplicateEndpointPerTenant()
    {
        var tenantId = Guid.NewGuid();
        await using (var context = NewContext())
            await new PushSubscriptionStore(context).AddAsync(NewSubscription(tenantId, "https://push.example.com/dup"), CancellationToken.None);

        await using var context2 = NewContext();
        await Assert.ThrowsAsync<DuplicateSubscriptionException>(() =>
            new PushSubscriptionStore(context2).AddAsync(NewSubscription(tenantId, "https://push.example.com/dup"), CancellationToken.None));
    }

    [Fact]
    public async Task List_IsScopedToTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using (var context = NewContext())
        {
            var store = new PushSubscriptionStore(context);
            await store.AddAsync(NewSubscription(tenantA, "https://push.example.com/a"), CancellationToken.None);
            await store.AddAsync(NewSubscription(tenantB, "https://push.example.com/b"), CancellationToken.None);
        }

        await using var read = NewContext();
        var list = await new PushSubscriptionStore(read).ListAsync(tenantA, CancellationToken.None);

        Assert.Single(list);
    }

    [Fact]
    public async Task Delete_RemovesSubscription_AndReturnsFalseWhenMissing()
    {
        var tenantId = Guid.NewGuid();
        var subscription = NewSubscription(tenantId, "https://push.example.com/x");
        await using (var context = NewContext())
            await new PushSubscriptionStore(context).AddAsync(subscription, CancellationToken.None);

        await using var context2 = NewContext();
        var store = new PushSubscriptionStore(context2);

        Assert.True(await store.DeleteAsync(tenantId, subscription.Id, CancellationToken.None));
        Assert.False(await store.DeleteAsync(tenantId, subscription.Id, CancellationToken.None));
    }
}
