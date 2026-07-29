using Hiram.Domain.Outbox;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Persistence;

public sealed class OutboxQueueTests : IAsyncLifetime
{
    private static readonly Guid DevTenant = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private DbContextOptions<HiramDbContext>? _options;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<HiramDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var context = NewContext();
        await HiramSchema.ApplyAsync(context);
    }

    public async Task DisposeAsync() =>
        await _postgres.DisposeAsync();

    [Fact]
    public async Task Claim_ConcurrentWorkersReceiveDisjointBatches()
    {
        var ids = await Seed(4);
        await using var firstContext = NewContext();
        await using var secondContext = NewContext();
        var firstQueue = new OutboxQueue(firstContext);
        var secondQueue = new OutboxQueue(secondContext);

        var batches = await Task.WhenAll(
            firstQueue.ClaimAsync(2, Now, TimeSpan.FromMinutes(1), CancellationToken.None),
            secondQueue.ClaimAsync(2, Now, TimeSpan.FromMinutes(1), CancellationToken.None));

        Assert.Equal(2, batches[0].Count);
        Assert.Equal(2, batches[1].Count);
        Assert.Empty(batches[0].Select(message => message.Id).Intersect(batches[1].Select(message => message.Id)));
        Assert.Equal(ids.Order(), batches.SelectMany(batch => batch).Select(message => message.Id).Order());
    }

    [Fact]
    public async Task Claim_ExpiredLeaseBecomesEligibleAndIncrementsAttempt()
    {
        var messageId = Assert.Single(await Seed(1));
        await using var context = NewContext();
        var queue = new OutboxQueue(context);

        var first = Assert.Single(await queue.ClaimAsync(1, Now, TimeSpan.FromMinutes(1), CancellationToken.None));
        var beforeExpiry = await queue.ClaimAsync(1, Now.AddSeconds(30), TimeSpan.FromMinutes(1), CancellationToken.None);
        var recovered = Assert.Single(await queue.ClaimAsync(1, Now.AddMinutes(2), TimeSpan.FromMinutes(1), CancellationToken.None));

        Assert.Empty(beforeExpiry);
        Assert.Equal(messageId, recovered.Id);
        Assert.Equal(1, first.AttemptCount);
        Assert.Equal(2, recovered.AttemptCount);
    }

    [Fact]
    public async Task ScheduleRetry_HoldsMessageUntilAvailableAtAndPreservesError()
    {
        var messageId = Assert.Single(await Seed(1));
        await using var context = NewContext();
        var queue = new OutboxQueue(context);
        var lease = Assert.Single(await queue.ClaimAsync(1, Now, TimeSpan.FromMinutes(1), CancellationToken.None));
        var retryAt = Now.AddMinutes(10);

        Assert.True(await queue.ScheduleRetryAsync(
            messageId, lease.LeaseUntil, Now, retryAt, "provider unavailable", CancellationToken.None));
        Assert.Empty(await queue.ClaimAsync(1, Now.AddMinutes(5), TimeSpan.FromMinutes(1), CancellationToken.None));

        context.ChangeTracker.Clear();
        var stored = await context.OutboxMessages.AsNoTracking().SingleAsync(message => message.Id == messageId);
        Assert.Equal(retryAt, stored.AvailableAt);
        Assert.Equal("provider unavailable", stored.LastError);
        Assert.Null(stored.LeaseUntil);

        var retried = Assert.Single(await queue.ClaimAsync(
            1, Now.AddMinutes(11), TimeSpan.FromMinutes(1), CancellationToken.None));
        Assert.Equal(2, retried.AttemptCount);
    }

    [Fact]
    public async Task ExpiredOwner_CannotMutateMessageAfterAnotherWorkerReclaimsIt()
    {
        var messageId = Assert.Single(await Seed(1));
        await using var context = NewContext();
        var queue = new OutboxQueue(context);
        var expired = Assert.Single(await queue.ClaimAsync(1, Now, TimeSpan.FromMinutes(1), CancellationToken.None));
        var reclaimedAt = Now.AddMinutes(2);
        var current = Assert.Single(await queue.ClaimAsync(
            1, reclaimedAt, TimeSpan.FromMinutes(5), CancellationToken.None));

        Assert.False(await queue.RenewAsync(
            messageId, expired.LeaseUntil, reclaimedAt, TimeSpan.FromMinutes(1), CancellationToken.None));
        Assert.False(await queue.CompleteAsync(
            messageId, expired.LeaseUntil, reclaimedAt, CancellationToken.None));
        Assert.False(await queue.ScheduleRetryAsync(
            messageId, expired.LeaseUntil, reclaimedAt, reclaimedAt.AddMinutes(1), "stale", CancellationToken.None));

        context.ChangeTracker.Clear();
        var stored = await context.OutboxMessages.AsNoTracking().SingleAsync(message => message.Id == messageId);
        Assert.Equal(current.LeaseUntil, stored.LeaseUntil);
        Assert.Null(stored.ProcessedAtUtc);
        Assert.Null(stored.LastError);
    }

    [Fact]
    public async Task CurrentOwner_CanRenewAndCompleteLease()
    {
        var messageId = Assert.Single(await Seed(1));
        await using var context = NewContext();
        var queue = new OutboxQueue(context);
        var lease = Assert.Single(await queue.ClaimAsync(1, Now, TimeSpan.FromMinutes(1), CancellationToken.None));
        var renewalTime = Now.AddSeconds(30);
        var renewedUntil = renewalTime.AddMinutes(5);

        Assert.True(await queue.RenewAsync(
            messageId, lease.LeaseUntil, renewalTime, TimeSpan.FromMinutes(5), CancellationToken.None));
        Assert.True(await queue.CompleteAsync(
            messageId, renewedUntil, Now.AddMinutes(1), CancellationToken.None));

        context.ChangeTracker.Clear();
        var stored = await context.OutboxMessages.AsNoTracking().SingleAsync(message => message.Id == messageId);
        Assert.Equal(Now.AddMinutes(1), stored.ProcessedAtUtc);
        Assert.Null(stored.LeaseUntil);
    }

    private async Task<IReadOnlyList<Guid>> Seed(int count)
    {
        var messages = Enumerable.Range(0, count)
            .Select(index => new OutboxMessage(
                Guid.NewGuid(),
                DevTenant,
                "email",
                "{}",
                Now.AddMinutes(-1).AddSeconds(index)))
            .ToList();

        await using var context = NewContext();
        context.OutboxMessages.AddRange(messages);
        await context.SaveChangesAsync();
        return messages.Select(message => message.Id).ToList();
    }

    private HiramDbContext NewContext() => new(_options!);
}
