namespace Hiram.Domain.Notifications;

public enum DeliveryOutcome
{
    Sent = 1,
    TransientFailure = 2,
    PermanentFailure = 3
}
