using Hiram.Domain.DeadLetters;
using Hiram.Domain.Notifications;

namespace Hiram.UnitTests.DeadLetters;

public class DeadLetterMessageTests
{
    private static DeadLetterMessage CreateValid() => new(
        id: Guid.NewGuid(),
        tenantId: Guid.NewGuid(),
        notificationId: Guid.NewGuid(),
        channel: NotificationChannel.Email,
        payload: "{\"NotificationId\":\"x\"}",
        reason: "exhausted_transient:connection refused",
        attemptCount: 3,
        createdAtUtc: DateTimeOffset.UnixEpoch);

    [Fact]
    public void Constructor_StoresProvidedValues()
    {
        var message = CreateValid();

        Assert.Equal(NotificationChannel.Email, message.Channel);
        Assert.Equal(3, message.AttemptCount);
        Assert.False(message.IsReplayed);
        Assert.Null(message.ReplayedAtUtc);
    }

    [Fact]
    public void Constructor_Throws_WhenNotificationIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new DeadLetterMessage(
            Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, NotificationChannel.Email, "{}", "r", 1, DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenReasonIsBlank(string reason)
    {
        Assert.Throws<ArgumentException>(() => new DeadLetterMessage(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), NotificationChannel.Email, "{}", reason, 1, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Constructor_Throws_WhenAttemptCountBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeadLetterMessage(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), NotificationChannel.Email, "{}", "r", 0, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void MarkReplayed_SetsTimestamp()
    {
        var message = CreateValid();
        var when = DateTimeOffset.UnixEpoch.AddMinutes(5);

        message.MarkReplayed(when);

        Assert.True(message.IsReplayed);
        Assert.Equal(when, message.ReplayedAtUtc);
    }

    [Fact]
    public void MarkReplayed_Throws_WhenAlreadyReplayed()
    {
        var message = CreateValid();
        message.MarkReplayed(DateTimeOffset.UnixEpoch.AddMinutes(5));

        Assert.Throws<InvalidOperationException>(() => message.MarkReplayed(DateTimeOffset.UnixEpoch.AddMinutes(6)));
    }
}
