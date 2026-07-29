using Hiram.Domain.Notifications;
using Hiram.Domain.Outbox;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Persistence;

public class NotificationStoreTests : IAsyncLifetime
{
    private static readonly Guid DevTenant = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .Build();

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

    private static NotificationRequest NewRequest(Guid id) =>
        new(id, DevTenant, NotificationChannel.Email, "felipe@example.com", "hello", "first slice", DateTimeOffset.UtcNow);

    private static OutboxMessage NewOutbox(Guid id) =>
        new(id, DevTenant, "email", "{}", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Save_WritesRequestAndOutboxRowTogether()
    {
        var requestId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();

        await using (var context = NewContext())
        {
            var store = new NotificationStore(context);
            await store.SaveAsync(NewRequest(requestId), NewOutbox(outboxId), CancellationToken.None);
        }

        await using var verify = NewContext();
        Assert.NotNull(await verify.NotificationRequests.FindAsync(requestId));
        Assert.NotNull(await verify.OutboxMessages.FindAsync(outboxId));
    }

    [Fact]
    public async Task Save_RollsBackRequest_WhenOutboxInsertFails()
    {
        var blockingOutboxId = Guid.NewGuid();

        // A committed outbox row whose id the failing save will collide with.
        await using (var seed = NewContext())
        {
            seed.OutboxMessages.Add(NewOutbox(blockingOutboxId));
            await seed.SaveChangesAsync();
        }

        var requestId = Guid.NewGuid();

        await using (var context = NewContext())
        {
            var store = new NotificationStore(context);
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                store.SaveAsync(NewRequest(requestId), NewOutbox(blockingOutboxId), CancellationToken.None));
        }

        await using var verify = NewContext();
        Assert.Null(await verify.NotificationRequests.FindAsync(requestId));
    }
}
