namespace Hiram.Application.Notifications;

// Raised by the store when the partial unique index rejects a second notification for the same
// (tenant, idempotency key). The handler turns it into a replay of the original notification.
public sealed class DuplicateIdempotencyKeyException : Exception
{
    public DuplicateIdempotencyKeyException()
        : base("A notification with this idempotency key already exists for the tenant.")
    {
    }
}
