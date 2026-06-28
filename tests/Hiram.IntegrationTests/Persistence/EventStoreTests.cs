using Hiram.Application.Events;
using Hiram.Domain.Events;
using Hiram.Domain.Outbox;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Persistence;

public class EventStoreTests : IAsyncLifetime
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

    private static NotificationEvent NewEvent(Guid id, string eventId) =>
        new(id, Tenant, "produto_vencendo", eventId, 1, "{}", DateTimeOffset.UtcNow);

    private static OutboxMessage NewOutbox(Guid id) =>
        new(id, Tenant, "event", "{}", DateTimeOffset.UtcNow);

    [Fact]
    public async Task EventIngestion_PersistsEventAndOutbox_InSameTransaction()
    {
        var eventRowId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();

        await using (var context = NewContext())
        {
            var store = new EventStore(context);
            await store.SaveAsync(NewEvent(eventRowId, "evt-1"), NewOutbox(outboxId), CancellationToken.None);
        }

        await using var verify = NewContext();
        Assert.NotNull(await verify.Events.FindAsync(eventRowId));
        Assert.NotNull(await verify.OutboxMessages.FindAsync(outboxId));
    }

    [Fact]
    public async Task DuplicateEventId_ThrowsAndDoesNotRefanout()
    {
        await using (var seed = NewContext())
        {
            var store = new EventStore(seed);
            await store.SaveAsync(NewEvent(Guid.NewGuid(), "dup"), NewOutbox(Guid.NewGuid()), CancellationToken.None);
        }

        var secondOutbox = Guid.NewGuid();
        await using (var context = NewContext())
        {
            var store = new EventStore(context);
            await Assert.ThrowsAsync<DuplicateEventException>(() =>
                store.SaveAsync(NewEvent(Guid.NewGuid(), "dup"), NewOutbox(secondOutbox), CancellationToken.None));
        }

        // The conflicting transaction rolled back: no second outbox row, so no re-fan-out.
        await using var verify = NewContext();
        Assert.Null(await verify.OutboxMessages.FindAsync(secondOutbox));
    }
}
