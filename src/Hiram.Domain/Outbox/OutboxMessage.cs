namespace Hiram.Domain.Outbox;

public sealed class OutboxMessage
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Type { get; private set; }
    public string Payload { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    // Parameterless ctor exists so EF Core can materialize rows without re-running creation invariants.
    private OutboxMessage()
    {
        Type = null!;
        Payload = null!;
    }

    public OutboxMessage(Guid id, Guid tenantId, string type, string payload, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Outbox message id is required.", nameof(id));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("Outbox message type is required.", nameof(type));
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("Outbox message payload is required.", nameof(payload));

        Id = id;
        TenantId = tenantId;
        Type = type;
        Payload = payload;
        CreatedAtUtc = createdAtUtc;
        ProcessedAtUtc = null;
    }

    public bool IsProcessed => ProcessedAtUtc is not null;

    public void MarkProcessed(DateTimeOffset processedAtUtc)
    {
        if (IsProcessed)
            throw new InvalidOperationException("Outbox message is already processed.");

        ProcessedAtUtc = processedAtUtc;
    }
}
