namespace Hiram.Domain.Notifications;

public sealed class DeliveryAttempt
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid NotificationId { get; private set; }
    public int AttemptNumber { get; private set; }
    public string Provider { get; private set; }
    public DeliveryOutcome Outcome { get; private set; }
    public string? Error { get; private set; }
    public TimeSpan Duration { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    // True when the tenant runs in shadow mode and the provider was resolved but never called.
    public bool Shadowed { get; private set; }

    // Hash of the message that would have been sent, kept for shadow parity audits without storing the body.
    public string? PayloadHash { get; private set; }

    // The provider's id for the accepted message (Resend's id, later WhatsApp's wamid). Present only on a
    // successful send, and only when the provider returns one; it is what a status callback matches back on.
    public string? ProviderMessageId { get; private set; }

    // Parameterless ctor exists so EF Core can materialize rows without re-running creation invariants.
    private DeliveryAttempt()
    {
        Provider = null!;
    }

    public DeliveryAttempt(
        Guid id,
        Guid tenantId,
        Guid notificationId,
        int attemptNumber,
        string provider,
        DeliveryOutcome outcome,
        string? error,
        TimeSpan duration,
        DateTimeOffset createdAtUtc,
        bool shadowed = false,
        string? payloadHash = null,
        string? providerMessageId = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Delivery attempt id is required.", nameof(id));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (notificationId == Guid.Empty)
            throw new ArgumentException("Notification id is required.", nameof(notificationId));
        if (attemptNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), attemptNumber, "Attempt number starts at 1.");
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider is required.", nameof(provider));
        if (!Enum.IsDefined(outcome))
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown delivery outcome.");
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Duration cannot be negative.");

        Id = id;
        TenantId = tenantId;
        NotificationId = notificationId;
        AttemptNumber = attemptNumber;
        Provider = provider;
        Outcome = outcome;
        Error = error;
        Duration = duration;
        CreatedAtUtc = createdAtUtc;
        Shadowed = shadowed;
        PayloadHash = payloadHash;
        ProviderMessageId = providerMessageId;
    }
}
