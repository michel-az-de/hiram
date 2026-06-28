using System.Text;
using Hiram.Application.Abstractions;
using Hiram.Domain.Outbox;
using Hiram.Infrastructure.Messaging;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Hiram.IntegrationTests.Messaging;

public class OutboxRelayTests : IAsyncLifetime
{
    private static readonly Guid DevTenant = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management")
        .WithUsername("hiram")
        .WithPassword("hiram")
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());
        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    private HiramDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<HiramDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new HiramDbContext(options);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    [Fact]
    public async Task RelayPending_PublishesMessageAndMarksRowProcessed()
    {
        var outboxId = Guid.NewGuid();

        await using (var seed = NewContext())
        {
            seed.OutboxMessages.Add(new OutboxMessage(
                outboxId, DevTenant, HiramTopology.EmailRoutingKey,
                "{\"recipient\":\"felipe@example.com\"}", DateTimeOffset.UtcNow));
            await seed.SaveChangesAsync();
        }

        string storedPayload;
        await using (var read = NewContext())
        {
            var stored = await read.OutboxMessages.AsNoTracking().FirstAsync(x => x.Id == outboxId);
            storedPayload = stored.Payload;
        }

        await using var connection = new RabbitMqConnection(_rabbit.GetConnectionString(), NullLogger<RabbitMqConnection>.Instance);

        int relayed;
        await using (var context = NewContext())
        {
            var relay = new OutboxRelay(context, connection, new FixedClock(DateTimeOffset.UtcNow), NullLogger<OutboxRelay>.Instance);
            relayed = await relay.RelayPendingAsync(CancellationToken.None);
        }

        Assert.Equal(1, relayed);

        await using (var verify = NewContext())
        {
            var row = await verify.OutboxMessages.FindAsync(outboxId);
            Assert.NotNull(row);
            Assert.NotNull(row!.ProcessedAtUtc);
        }

        var published = await ReadFromQueueAsync(connection, HiramTopology.EmailQueue);
        Assert.Equal(storedPayload, published);
    }

    [Fact]
    public async Task DirectNotification_StillPublishesImmediately_WithDeferralQuery()
    {
        var directId = Guid.NewGuid();
        var deferredId = Guid.NewGuid();

        await using (var seed = NewContext())
        {
            seed.OutboxMessages.Add(new OutboxMessage(
                directId, DevTenant, HiramTopology.EmailRoutingKey, "{}", DateTimeOffset.UtcNow));
            seed.OutboxMessages.Add(new OutboxMessage(
                deferredId, DevTenant, HiramTopology.EmailRoutingKey, "{}", DateTimeOffset.UtcNow,
                traceParent: null, dispatchAt: DateTimeOffset.UtcNow.AddHours(2)));
            await seed.SaveChangesAsync();
        }

        await using var connection = new RabbitMqConnection(_rabbit.GetConnectionString(), NullLogger<RabbitMqConnection>.Instance);

        int relayed;
        await using (var context = NewContext())
        {
            var relay = new OutboxRelay(context, connection, new FixedClock(DateTimeOffset.UtcNow), NullLogger<OutboxRelay>.Instance);
            relayed = await relay.RelayPendingAsync(CancellationToken.None);
        }

        // The direct row (dispatch_at null) publishes immediately; the deferred row is held.
        Assert.Equal(1, relayed);

        await using var verify = NewContext();
        var direct = await verify.OutboxMessages.FindAsync(directId);
        var deferred = await verify.OutboxMessages.FindAsync(deferredId);
        Assert.NotNull(direct!.ProcessedAtUtc);
        Assert.Null(deferred!.ProcessedAtUtc);
    }

    private static async Task<string?> ReadFromQueueAsync(RabbitMqConnection connection, string queue)
    {
        var rabbit = await connection.GetConnectionAsync(CancellationToken.None);
        await using var channel = await rabbit.CreateChannelAsync();

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var result = await channel.BasicGetAsync(queue, autoAck: true);
            if (result is not null)
                return Encoding.UTF8.GetString(result.Body.Span);

            await Task.Delay(100);
        }

        return null;
    }
}
