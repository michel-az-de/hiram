using Hiram.Domain.Consents;
using Hiram.Domain.Notifications;
using Hiram.Domain.Routines;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Persistence;

public class ConsentStoreTests : IAsyncLifetime
{
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid User = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

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
    public async Task Upsert_InsertsThenUpdatesInPlace_KeyedByUserChannelCategory()
    {
        var store = new ConsentStore(NewContext());
        var t0 = DateTimeOffset.UtcNow;

        await store.UpsertAsync(
            new Consent(Guid.NewGuid(), Tenant, User, NotificationChannel.Email, NotificationCategory.Marketing, optIn: true, t0),
            CancellationToken.None);

        // Same key, later opt-out: must update in place, not duplicate.
        await new ConsentStore(NewContext()).UpsertAsync(
            new Consent(Guid.NewGuid(), Tenant, User, NotificationChannel.Email, NotificationCategory.Marketing, optIn: false, t0.AddMinutes(1)),
            CancellationToken.None);

        await using var verify = NewContext();
        var rows = await verify.Consents.Where(c => c.TenantId == Tenant && c.UserId == User).ToListAsync();
        Assert.Single(rows);
        Assert.False(rows[0].OptIn);
    }
}
