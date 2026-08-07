using System.Text.Json;
using Hiram.Application.Abstractions;
using Hiram.Application.Blocks;
using Hiram.Application.Delivery;
using Hiram.Application.Notifications;
using Hiram.Application.Push;
using Hiram.Domain.Blocks;
using Hiram.Domain.DeadLetters;
using Hiram.Domain.Notifications;
using Hiram.Domain.Push;
using Hiram.Domain.Tenants;
using Hiram.Infrastructure.Messaging;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Retry;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Push;

public class PushDeliveryPipelineTests : IAsyncLifetime
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

    private sealed class ScriptedPushSender(IEnumerable<SendOutcome> outcomes) : IPushSender
    {
        private readonly Queue<SendOutcome> _outcomes = new(outcomes);
        public int Calls { get; private set; }

        public Task<SendOutcome> SendAsync(PushSubscription subscription, string payload, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_outcomes.Count > 0 ? _outcomes.Dequeue() : new SendOutcome.PermanentFailure("no more scripted outcomes"));
        }
    }

    private sealed class NoBlocks : IBlockStore
    {
        public Task<IReadOnlyList<Block>> ActiveBlocksAsync(Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Block>>([]);

        public Task AddAsync(Block block, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> RemoveAsync(Guid tenantId, Guid id, DateTimeOffset removedAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class ChannelBlocked(NotificationChannel channel) : IBlockStore
    {
        public Task<IReadOnlyList<Block>> ActiveBlocksAsync(Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Block>>([new Block(Guid.NewGuid(), tenantId, channel, "incident", now, expiresAtUtc: null)]);

        public Task AddAsync(Block block, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> RemoveAsync(Guid tenantId, Guid id, DateTimeOffset removedAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private static ResiliencePipeline<SendOutcome> FastPipeline() =>
        new ResiliencePipelineBuilder<SendOutcome>()
            .AddRetry(new RetryStrategyOptions<SendOutcome>
            {
                ShouldHandle = new PredicateBuilder<SendOutcome>().HandleResult(outcome => outcome is SendOutcome.TransientFailure),
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(1),
                BackoffType = DelayBackoffType.Constant,
                UseJitter = false
            })
            .Build();

    private static ChannelDeliveryProcessor BuildProcessor(HiramDbContext context, IBlockStore? blocks = null) =>
        new(context, new BlockGate(blocks ?? new NoBlocks()), FastPipeline(), new TestClock(),
            NullLogger<ChannelDeliveryProcessor>.Instance);

    private static Task Process(HiramDbContext context, IPushSender sender, byte[] payload, IBlockStore? blocks = null) =>
        BuildProcessor(context, blocks).ProcessAsync(new PushChannelDelivery(context, sender), payload, CancellationToken.None);

    private async Task<(Guid TenantId, Guid NotificationId, Guid SubscriptionId)> Seed(DeliveryMode mode, bool withSubscription = true)
    {
        var tenantId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        await using var context = NewContext();
        context.Tenants.Add(new Tenant(tenantId, "test-tenant", mode, DateTimeOffset.UtcNow));
        if (withSubscription)
            context.PushSubscriptions.Add(new PushSubscription(
                subscriptionId, tenantId, "https://push.example.com/x", "p256dh", "auth", DateTimeOffset.UtcNow));
        context.NotificationRequests.Add(new NotificationRequest(
            notificationId, tenantId, NotificationChannel.Push, subscriptionId.ToString(), "title", "body", DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
        return (tenantId, notificationId, subscriptionId);
    }

    private static byte[] PayloadFor(Guid notificationId, Guid tenantId, Guid subscriptionId) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new OutboxNotificationPayload(notificationId, tenantId, "push", subscriptionId.ToString(), "title", "body"));

    private async Task<NotificationStatus> StatusOf(Guid notificationId)
    {
        await using var context = NewContext();
        return (await context.NotificationRequests.FindAsync(notificationId))!.Status;
    }

    private async Task<DeadLetterMessage?> DeadLetterFor(Guid notificationId)
    {
        await using var context = NewContext();
        return await context.DeadLetterMessages
            .Where(d => d.NotificationId == notificationId)
            .OrderByDescending(d => d.CreatedAtUtc)
            .FirstOrDefaultAsync();
    }

    [Fact]
    public async Task Process_Sends_WhenSubscriptionResolves()
    {
        var (tenantId, notificationId, subscriptionId) = await Seed(DeliveryMode.Live);
        var sender = new ScriptedPushSender([new SendOutcome.Sent()]);

        await using (var context = NewContext())
            await Process(context, sender, PayloadFor(notificationId, tenantId, subscriptionId));

        Assert.Equal(NotificationStatus.Sent, await StatusOf(notificationId));
        Assert.Equal(1, sender.Calls);
    }

    [Fact]
    public async Task Process_Suppresses_WithoutCallingSender_WhenChannelBlocked()
    {
        var (tenantId, notificationId, subscriptionId) = await Seed(DeliveryMode.Live);
        // Scripted to a hard failure: if the sender were ever called, the assertions below would catch it.
        var sender = new ScriptedPushSender([new SendOutcome.PermanentFailure("must not be called")]);

        await using (var context = NewContext())
            await Process(context, sender, PayloadFor(notificationId, tenantId, subscriptionId), new ChannelBlocked(NotificationChannel.Push));

        Assert.Equal(NotificationStatus.Suppressed, await StatusOf(notificationId));
        Assert.Equal(0, sender.Calls);
    }

    [Fact]
    public async Task Process_IsNoOp_WhenSuppressedPushIsRedelivered()
    {
        var (tenantId, notificationId, subscriptionId) = await Seed(DeliveryMode.Live);
        var sender = new ScriptedPushSender([new SendOutcome.PermanentFailure("must not be called")]);
        var payload = PayloadFor(notificationId, tenantId, subscriptionId);

        await using (var context = NewContext())
            await Process(context, sender, payload, new ChannelBlocked(NotificationChannel.Push));

        await using (var context = NewContext())
            await Process(context, sender, payload);

        Assert.Equal(NotificationStatus.Suppressed, await StatusOf(notificationId));
        Assert.Equal(0, sender.Calls);
    }

    [Fact]
    public async Task Process_Delivers_Unchanged_WhenNoBlockActive()
    {
        // ADR-024 regression guard: with no active block the push path is exactly what it was.
        var (tenantId, notificationId, subscriptionId) = await Seed(DeliveryMode.Live);
        var sender = new ScriptedPushSender([new SendOutcome.Sent()]);

        await using (var context = NewContext())
            await Process(context, sender, PayloadFor(notificationId, tenantId, subscriptionId));

        Assert.Equal(NotificationStatus.Sent, await StatusOf(notificationId));
        Assert.Equal(1, sender.Calls);
    }

    [Fact]
    public async Task Process_DeadLetters_WhenSubscriptionMissing()
    {
        var (tenantId, notificationId, subscriptionId) = await Seed(DeliveryMode.Live, withSubscription: false);
        var sender = new ScriptedPushSender([new SendOutcome.Sent()]);

        await using (var context = NewContext())
            await Process(context, sender, PayloadFor(notificationId, tenantId, subscriptionId));

        Assert.Equal(NotificationStatus.DeadLettered, await StatusOf(notificationId));
        Assert.Equal(0, sender.Calls);
        var deadLetter = await DeadLetterFor(notificationId);
        Assert.Contains("subscription_not_found", deadLetter!.Reason);
    }

    [Fact]
    public async Task Process_DeadLetters_AfterThreeAttempts_WhenAlwaysTransient()
    {
        var (tenantId, notificationId, subscriptionId) = await Seed(DeliveryMode.Live);
        var sender = new ScriptedPushSender(Enumerable.Repeat<SendOutcome>(new SendOutcome.TransientFailure("503"), 5));

        await using (var context = NewContext())
            await Process(context, sender, PayloadFor(notificationId, tenantId, subscriptionId));

        Assert.Equal(NotificationStatus.DeadLettered, await StatusOf(notificationId));
        Assert.Equal(3, sender.Calls);
        var deadLetter = await DeadLetterFor(notificationId);
        Assert.Equal(3, deadLetter!.AttemptCount);
    }

    [Fact]
    public async Task Process_Shadows_WithoutCallingSender()
    {
        var (tenantId, notificationId, subscriptionId) = await Seed(DeliveryMode.Shadow);
        var sender = new ScriptedPushSender([new SendOutcome.PermanentFailure("must not be called")]);

        await using (var context = NewContext())
            await Process(context, sender, PayloadFor(notificationId, tenantId, subscriptionId));

        Assert.Equal(0, sender.Calls);
        Assert.Equal(NotificationStatus.Sent, await StatusOf(notificationId));

        await using var verify = NewContext();
        var attempt = await verify.DeliveryAttempts.SingleAsync(a => a.NotificationId == notificationId);
        Assert.Equal(DeliveryOutcome.ShadowWouldSend, attempt.Outcome);
        Assert.True(attempt.Shadowed);
    }
}
