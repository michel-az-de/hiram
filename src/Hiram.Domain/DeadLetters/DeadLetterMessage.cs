using Hiram.Domain.Notifications;

namespace Hiram.Domain.DeadLetters;

public sealed class DeadLetterMessage
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid NotificationId { get; private set; }
    public NotificationChannel Channel { get; private set; }

    // The exact outbox payload that was attempted, so a replay reproduces what failed and not a re-render.
    public string Payload { get; private set; }
    public string Reason { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ReplayedAtUtc { get; private set; }

    public bool IsReplayed => ReplayedAtUtc is not null;

    // Parameterless ctor exists so EF Core can materialize rows without re-running creation invariants.
    private DeadLetterMessage()
    {
        Payload = null!;
        Reason = null!;
    }

    public DeadLetterMessage(
        Guid id,
        Guid tenantId,
        Guid notificationId,
        NotificationChannel channel,
        string payload,
        string reason,
        int attemptCount,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Dead letter id is required.", nameof(id));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (notificationId == Guid.Empty)
            throw new ArgumentException("Notification id is required.", nameof(notificationId));
        if (!Enum.IsDefined(channel))
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown notification channel.");
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("Payload is required.", nameof(payload));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason is required.", nameof(reason));
        if (attemptCount < 1)
            throw new ArgumentOutOfRangeException(nameof(attemptCount), attemptCount, "Attempt count starts at 1.");

        Id = id;
        TenantId = tenantId;
        NotificationId = notificationId;
        Channel = channel;
        Payload = payload;
        Reason = reason;
        AttemptCount = attemptCount;
        CreatedAtUtc = createdAtUtc;
    }

    public void MarkReplayed(DateTimeOffset whenUtc)
    {
        if (IsReplayed)
            throw new InvalidOperationException("Dead letter message has already been replayed.");

        ReplayedAtUtc = whenUtc;
    }
}
