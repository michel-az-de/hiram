namespace Hiram.Domain.Events;

public enum EventStatus
{
    Accepted,
    Routed,
    NoRoute,
    Suppressed
}
