using System.Text.Json;
using Hiram.Application.Abstractions;
using Hiram.Application.Notifications;
using Hiram.Domain.DeadLetters;
using Hiram.Domain.Notifications;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Replay;

public class DeadLetterReplayTests : IAsyncLifetime
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

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private static DeadLetterMessage NewDeadLetter(Guid tenantId, Guid notificationId, string reason, int attempts) => new(
        Guid.NewGuid(), tenantId, notificationId, NotificationChannel.Email,
        JsonSerializer.Serialize(new OutboxNotificationPayload(notificationId, tenantId, "email", "ops@example.com", "s", "b")),
        reason, attempts, DateTimeOffset.UtcNow);

    private async Task<(Guid TenantId, Guid NotificationId)> SeedDeadLettered()
    {
        var tenantId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        await using var context = NewContext();

        var notification = new NotificationRequest(
            notificationId, tenantId, NotificationChannel.Email, "ops@example.com", "s", "b", DateTimeOffset.UtcNow);
        notification.MarkSending();
        notification.MarkDeadLettered();

        context.NotificationRequests.Add(notification);
        context.DeadLetterMessages.Add(NewDeadLetter(tenantId, notificationId, "exhausted_transient:timeout", 3));
        await context.SaveChangesAsync();
        return (tenantId, notificationId);
    }

    [Fact]
    public async Task ConcurrentReplay_WritesOneOutbox_AndSecondConflicts()
    {
        var (tenantId, notificationId) = await SeedDeadLettered();

        await using var contextA = NewContext();
        await using var contextB = NewContext();

        var results = await Task.WhenAll(
            new DeadLetterReplay(contextA, new TestClock()).ReplayAsync(tenantId, notificationId, CancellationToken.None),
            new DeadLetterReplay(contextB, new TestClock()).ReplayAsync(tenantId, notificationId, CancellationToken.None));

        Assert.Contains(ReplayOutcome.Replayed, results);
        Assert.Contains(ReplayOutcome.AlreadyReplayed, results);

        await using var verify = NewContext();
        Assert.Equal(1, await verify.OutboxMessages.CountAsync(o => o.TenantId == tenantId));
        Assert.Equal(NotificationStatus.Queued, (await verify.NotificationRequests.FindAsync(notificationId))!.Status);
        Assert.Equal(0, await verify.DeadLetterMessages.CountAsync(d => d.NotificationId == notificationId && d.ReplayedAtUtc == null));
    }

    [Fact]
    public async Task Replay_WritesExactlyOneOutbox_AndMarksDeadLetterReplayed()
    {
        var (tenantId, notificationId) = await SeedDeadLettered();

        await using (var context = NewContext())
        {
            var outcome = await new DeadLetterReplay(context, new TestClock()).ReplayAsync(tenantId, notificationId, CancellationToken.None);
            Assert.Equal(ReplayOutcome.Replayed, outcome);
        }

        await using var verify = NewContext();
        Assert.Equal(1, await verify.OutboxMessages.CountAsync(o => o.TenantId == tenantId));
        var deadLetter = await verify.DeadLetterMessages.SingleAsync(d => d.NotificationId == notificationId);
        Assert.True(deadLetter.IsReplayed);
        Assert.Equal(NotificationStatus.Queued, (await verify.NotificationRequests.FindAsync(notificationId))!.Status);
    }

    [Fact]
    public async Task Replay_Conflicts_WhenNotDeadLettered()
    {
        var tenantId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        await using (var seed = NewContext())
        {
            seed.NotificationRequests.Add(new NotificationRequest(
                notificationId, tenantId, NotificationChannel.Email, "ops@example.com", "s", "b", DateTimeOffset.UtcNow));
            await seed.SaveChangesAsync();
        }

        await using var context = NewContext();
        var outcome = await new DeadLetterReplay(context, new TestClock()).ReplayAsync(tenantId, notificationId, CancellationToken.None);

        Assert.Equal(ReplayOutcome.NotDeadLettered, outcome);
    }

    [Fact]
    public async Task Replay_NotFound_ForOtherTenant()
    {
        var (_, notificationId) = await SeedDeadLettered();

        await using var context = NewContext();
        var outcome = await new DeadLetterReplay(context, new TestClock()).ReplayAsync(Guid.NewGuid(), notificationId, CancellationToken.None);

        Assert.Equal(ReplayOutcome.NotFound, outcome);
    }

    [Fact]
    public async Task Replay_TargetsOpenDeadLetter_AcrossCycles()
    {
        var (tenantId, notificationId) = await SeedDeadLettered();

        await using (var context = NewContext())
            await new DeadLetterReplay(context, new TestClock()).ReplayAsync(tenantId, notificationId, CancellationToken.None);

        // The notification fails again: a fresh open dead letter is recorded and the status returns to DeadLettered.
        await using (var context = NewContext())
        {
            var notification = await context.NotificationRequests.FindAsync(notificationId);
            notification!.MarkSending();
            notification.MarkDeadLettered();
            context.DeadLetterMessages.Add(NewDeadLetter(tenantId, notificationId, "permanent_failure:rejected", 1));
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            var outcome = await new DeadLetterReplay(context, new TestClock()).ReplayAsync(tenantId, notificationId, CancellationToken.None);
            Assert.Equal(ReplayOutcome.Replayed, outcome);
        }

        await using var verify = NewContext();
        var deadLetters = await verify.DeadLetterMessages.Where(d => d.NotificationId == notificationId).ToListAsync();
        Assert.Equal(2, deadLetters.Count);
        Assert.All(deadLetters, d => Assert.True(d.IsReplayed));
        Assert.Equal(2, await verify.OutboxMessages.CountAsync(o => o.TenantId == tenantId));
    }
}
