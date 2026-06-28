namespace Hiram.Domain.Events;

// A raw business event ingested from a tenant (the EasyStok outbox). The engine resolves it into one or
// more rendered messages. EventId is the tenant's idempotency key; EmissionSeq is the tenant database
// sequence used as the cutover watermark.
public sealed class NotificationEvent
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string EventType { get; private set; }
    public string EventId { get; private set; }
    public long EmissionSeq { get; private set; }
    public string Payload { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public EventStatus Status { get; private set; }

    private NotificationEvent()
    {
        EventType = null!;
        EventId = null!;
        Payload = null!;
    }

    public NotificationEvent(
        Guid id,
        Guid tenantId,
        string eventType,
        string eventId,
        long emissionSeq,
        string payload,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Event id is required.", nameof(id));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type is required.", nameof(eventType));
        if (string.IsNullOrWhiteSpace(eventId))
            throw new ArgumentException("Event idempotency id is required.", nameof(eventId));
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("Event payload is required.", nameof(payload));

        Id = id;
        TenantId = tenantId;
        EventType = eventType;
        EventId = eventId;
        EmissionSeq = emissionSeq;
        Payload = payload;
        CreatedAtUtc = createdAtUtc;
        Status = EventStatus.Accepted;
    }
}
