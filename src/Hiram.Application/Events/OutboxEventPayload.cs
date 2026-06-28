namespace Hiram.Application.Events;

// What the routine engine consumes from the outbox to fan an event out into rendered messages.
public sealed record OutboxEventPayload(
    Guid EventRowId,
    Guid TenantId,
    string EventType,
    string EventId,
    long EmissionSeq,
    EventPayload Payload);
