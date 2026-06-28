using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Persistence;

public class MessageClaimStoreTests : IAsyncLifetime
{
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-0000-0000-000000000001");

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
    public async Task TryClaim_SucceedsOnce_ThenRejectsTheSameKey()
    {
        Assert.True(await new MessageClaimStore(NewContext()).TryClaimAsync(Tenant, "msg-key", CancellationToken.None));
        Assert.False(await new MessageClaimStore(NewContext()).TryClaimAsync(Tenant, "msg-key", CancellationToken.None));
    }
}
