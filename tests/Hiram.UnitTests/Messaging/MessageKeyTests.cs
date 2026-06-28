using Hiram.Application.Messaging;
using Hiram.Domain.Notifications;

namespace Hiram.UnitTests.Messaging;

public class MessageKeyTests
{
    [Fact]
    public void Compute_IsDeterministic_AndSensitiveToInputs()
    {
        var key = MessageKey.Compute("evt-1", NotificationChannel.Email, "x@y.com", 1, null);

        Assert.Equal(key, MessageKey.Compute("evt-1", NotificationChannel.Email, "x@y.com", 1, null));
        Assert.NotEqual(key, MessageKey.Compute("evt-1", NotificationChannel.Email, "x@y.com", 2, null));
        Assert.NotEqual(key, MessageKey.Compute("evt-1", NotificationChannel.Push, "x@y.com", 1, null));
        Assert.NotEqual(key, MessageKey.Compute("evt-1", NotificationChannel.Email, "other@y.com", 1, null));
        Assert.NotEqual(key, MessageKey.Compute("evt-1", NotificationChannel.Email, "x@y.com", 1, "2026-06-28"));
    }
}
