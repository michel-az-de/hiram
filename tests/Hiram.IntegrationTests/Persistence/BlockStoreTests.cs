using Hiram.Domain.Blocks;
using Hiram.Domain.Notifications;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Persistence;

public class BlockStoreTests : IAsyncLifetime
{
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherTenant = Guid.Parse("00000000-0000-0000-0000-000000000002");

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

    [Fact]
    public async Task ActiveBlocks_IncludesTenantAndGlobal_ExcludesExpiredRemovedAndOtherTenant()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new BlockStore(NewContext());

        await store.AddAsync(new Block(Guid.NewGuid(), Tenant, NotificationChannel.Email, "tenant block", now, null), CancellationToken.None);
        await store.AddAsync(new Block(Guid.NewGuid(), null, null, "global block", now, null), CancellationToken.None);
        await store.AddAsync(new Block(Guid.NewGuid(), Tenant, NotificationChannel.Email, "expired", now, now.AddMinutes(-1)), CancellationToken.None);
        await store.AddAsync(new Block(Guid.NewGuid(), OtherTenant, NotificationChannel.Email, "other tenant", now, null), CancellationToken.None);

        var removable = new Block(Guid.NewGuid(), Tenant, NotificationChannel.Push, "to remove", now, null);
        await store.AddAsync(removable, CancellationToken.None);
        await new BlockStore(NewContext()).RemoveAsync(Tenant, removable.Id, now, CancellationToken.None);

        var active = await new BlockStore(NewContext()).ActiveBlocksAsync(Tenant, now, CancellationToken.None);

        Assert.Equal(2, active.Count);
        Assert.All(active, b => Assert.True(b.TenantId == Tenant || b.TenantId is null));
    }
}
