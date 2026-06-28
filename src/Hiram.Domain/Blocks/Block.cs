using Hiram.Domain.Notifications;

namespace Hiram.Domain.Blocks;

// A kill switch. TenantId null means global (all tenants); Channel null means all channels. Optional
// expiry; an explicit removal ends it early.
public sealed class Block
{
    public Guid Id { get; private set; }
    public Guid? TenantId { get; private set; }
    public NotificationChannel? Channel { get; private set; }
    public string Reason { get; private set; }
    public DateTimeOffset ActivatedAtUtc { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RemovedAtUtc { get; private set; }

    private Block()
    {
        Reason = null!;
    }

    public Block(
        Guid id,
        Guid? tenantId,
        NotificationChannel? channel,
        string reason,
        DateTimeOffset activatedAtUtc,
        DateTimeOffset? expiresAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Block id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A block reason is required.", nameof(reason));

        Id = id;
        TenantId = tenantId;
        Channel = channel;
        Reason = reason;
        ActivatedAtUtc = activatedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public bool IsActive(DateTimeOffset now) =>
        RemovedAtUtc is null && (ExpiresAtUtc is null || ExpiresAtUtc > now);

    public void Remove(DateTimeOffset removedAtUtc) => RemovedAtUtc = removedAtUtc;
}
