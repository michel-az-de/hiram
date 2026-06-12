namespace Hiram.Domain.Notifications;

public enum NotificationStatus
{
    Accepted = 1,
    Queued = 2,
    Sending = 3,
    Sent = 4,
    Failed = 5,
    Suppressed = 6
}
