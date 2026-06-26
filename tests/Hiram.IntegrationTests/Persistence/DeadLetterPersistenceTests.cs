using Hiram.Domain.DeadLetters;
using Hiram.Domain.Notifications;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Persistence;

public class DeadLetterPersistenceTests : IAsyncLifetime
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

    private HiramDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<HiramDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new HiramDbContext(options);
    }

    private static DeadLetterMessage NewDeadLetter(Guid notificationId) => new(
        Guid.NewGuid(), Tenant, notificationId, NotificationChannel.Email,
        "{\"NotificationId\":\"x\"}", "exhausted_transient:timeout", attemptCount: 3, DateTimeOffset.UtcNow);

    [Fact]
    public async Task DeadLetter_RoundTripsThroughTheStore()
    {
        var notificationId = Guid.NewGuid();
        var deadLetter = NewDeadLetter(notificationId);

        await using (var seed = NewContext())
        {
            seed.DeadLetterMessages.Add(deadLetter);
            await seed.SaveChangesAsync();
        }

        await using var context = NewContext();
        var stored = await context.DeadLetterMessages.SingleAsync(x => x.NotificationId == notificationId);

        Assert.Equal(NotificationChannel.Email, stored.Channel);
        Assert.Equal("exhausted_transient:timeout", stored.Reason);
        Assert.Equal(3, stored.AttemptCount);
        Assert.False(stored.IsReplayed);
    }

    [Fact]
    public async Task OpenDeadLetterIndex_RejectsSecondUnreplayedForSameNotification()
    {
        var notificationId = Guid.NewGuid();

        await using (var seed = NewContext())
        {
            seed.DeadLetterMessages.Add(NewDeadLetter(notificationId));
            await seed.SaveChangesAsync();
        }

        await using var context = NewContext();
        context.DeadLetterMessages.Add(NewDeadLetter(notificationId));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task OpenDeadLetterIndex_AllowsNewCycleOncePreviousIsReplayed()
    {
        var notificationId = Guid.NewGuid();

        await using (var seed = NewContext())
        {
            var first = NewDeadLetter(notificationId);
            first.MarkReplayed(DateTimeOffset.UtcNow);
            seed.DeadLetterMessages.Add(first);
            await seed.SaveChangesAsync();
        }

        await using var context = NewContext();
        context.DeadLetterMessages.Add(NewDeadLetter(notificationId));

        var saved = await context.SaveChangesAsync();

        Assert.Equal(1, saved);
    }
}
