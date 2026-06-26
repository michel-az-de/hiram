namespace Hiram.Infrastructure.Messaging;

// Raised when a message can never be processed no matter how many times it is delivered: an unparseable
// payload or a notification that cannot exist. The consumer parks these instead of requeuing them.
public sealed class PoisonMessageException : Exception
{
    public PoisonMessageException(string message) : base(message)
    {
    }

    public PoisonMessageException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
