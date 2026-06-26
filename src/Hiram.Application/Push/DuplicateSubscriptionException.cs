namespace Hiram.Application.Push;

public sealed class DuplicateSubscriptionException : Exception
{
    public DuplicateSubscriptionException()
        : base("This endpoint is already registered for the tenant.")
    {
    }
}
