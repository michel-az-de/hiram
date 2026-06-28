namespace Hiram.Application.Events;

public sealed record IngestEventCommand(
    Guid TenantId,
    string EventType,
    string EventId,
    long EmissionSeq,
    EventPayload Payload);
