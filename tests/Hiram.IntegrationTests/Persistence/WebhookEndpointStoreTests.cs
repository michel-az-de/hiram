using Hiram.Application.Webhooks;
using Hiram.Domain.Webhooks;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Persistence;

public class WebhookEndpointStoreTests : IAsyncLifetime
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

    private static WebhookEndpoint NewEndpoint(Guid tenantId, string url) =>
        new(Guid.NewGuid(), tenantId, url, "protected:secret", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Add_List_And_HasAny_RoundTrip()
    {
        var tenantId = Guid.NewGuid();
        await using (var context = NewContext())
            await new WebhookEndpointStore(context).AddAsync(NewEndpoint(tenantId, "https://t.example.com/a"), CancellationToken.None);

        await using var read = NewContext();
        var store = new WebhookEndpointStore(read);

        var list = await store.ListAsync(tenantId, CancellationToken.None);
        Assert.Single(list);
        Assert.True(await store.HasAnyAsync(tenantId, CancellationToken.None));
        Assert.False(await store.HasAnyAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Add_Throws_OnDuplicateUrlPerTenant()
    {
        var tenantId = Guid.NewGuid();
        await using (var context = NewContext())
            await new WebhookEndpointStore(context).AddAsync(NewEndpoint(tenantId, "https://t.example.com/dup"), CancellationToken.None);

        await using var context2 = NewContext();
        await Assert.ThrowsAsync<DuplicateWebhookEndpointException>(() =>
            new WebhookEndpointStore(context2).AddAsync(NewEndpoint(tenantId, "https://t.example.com/dup"), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesEndpoint_AndReturnsFalseWhenMissing()
    {
        var tenantId = Guid.NewGuid();
        var endpoint = NewEndpoint(tenantId, "https://t.example.com/x");
        await using (var context = NewContext())
            await new WebhookEndpointStore(context).AddAsync(endpoint, CancellationToken.None);

        await using var context2 = NewContext();
        var store = new WebhookEndpointStore(context2);

        Assert.True(await store.DeleteAsync(tenantId, endpoint.Id, CancellationToken.None));
        Assert.False(await store.DeleteAsync(tenantId, endpoint.Id, CancellationToken.None));
    }
}
