namespace Hiram.Domain.Messaging;

// A durable claim on a rendered message's idempotency key. Its unique index is the arbiter that stops
// a replay or redelivery from sending twice.
public sealed class MessageClaim
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string MessageKey { get; private set; }
    public DateTimeOffset ClaimedAtUtc { get; private set; }

    private MessageClaim()
    {
        MessageKey = null!;
    }

    public MessageClaim(Guid id, Guid tenantId, string messageKey, DateTimeOffset claimedAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Claim id is required.", nameof(id));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(messageKey))
            throw new ArgumentException("Message key is required.", nameof(messageKey));

        Id = id;
        TenantId = tenantId;
        MessageKey = messageKey;
        ClaimedAtUtc = claimedAtUtc;
    }
}
