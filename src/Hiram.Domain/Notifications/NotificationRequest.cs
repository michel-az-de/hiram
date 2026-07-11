namespace Hiram.Domain.Notifications;

public sealed class NotificationRequest
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public string Recipient { get; private set; }
    public string Subject { get; private set; }
    public string Body { get; private set; }
    public NotificationStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    // Client supplied key that scopes idempotency to the tenant. Null when the client did not send one.
    public string? IdempotencyKey { get; private set; }

    // Parameterless ctor exists so EF Core can materialize rows without re-running creation invariants.
    private NotificationRequest()
    {
        Recipient = null!;
        Subject = null!;
        Body = null!;
    }

    public NotificationRequest(
        Guid id,
        Guid tenantId,
        NotificationChannel channel,
        string recipient,
        string subject,
        string body,
        DateTimeOffset createdAtUtc,
        string? idempotencyKey = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Notification id is required.", nameof(id));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (!Enum.IsDefined(channel))
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown notification channel.");
        if (string.IsNullOrWhiteSpace(recipient))
            throw new ArgumentException("Recipient is required.", nameof(recipient));
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Subject is required.", nameof(subject));
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Body is required.", nameof(body));

        Id = id;
        TenantId = tenantId;
        Channel = channel;
        Recipient = recipient;
        Subject = subject;
        Body = body;
        CreatedAtUtc = createdAtUtc;
        IdempotencyKey = idempotencyKey;
        Status = NotificationStatus.Accepted;
    }

    public void MarkSending()
    {
        Status = NotificationStatus.Sending;
    }

    public void MarkSent()
    {
        if (Status == NotificationStatus.Sent)
            return;

        Status = NotificationStatus.Sent;
    }

    public void MarkFailed()
    {
        Status = NotificationStatus.Failed;
    }

    public void MarkDeadLettered()
    {
        if (Status is not (NotificationStatus.Sending or NotificationStatus.Failed))
            throw new InvalidOperationException(
                $"Only a sending or failed notification can be dead lettered, current status is {Status}.");

        Status = NotificationStatus.DeadLettered;
    }

    public void MarkSuppressed()
    {
        // Suppression is a decision not to send, taken before delivery. Once the provider has been called
        // (Sending) or the outcome has settled (Sent, Failed, DeadLettered), the send cannot be un-done, so
        // those states are not suppressible.
        if (Status is NotificationStatus.Sending or NotificationStatus.Sent or NotificationStatus.Failed or NotificationStatus.DeadLettered)
            throw new InvalidOperationException(
                $"A notification that already reached {Status} cannot be suppressed.");

        Status = NotificationStatus.Suppressed;
    }

    public void RequeueForReplay()
    {
        if (Status != NotificationStatus.DeadLettered)
            throw new InvalidOperationException(
                $"Only a dead lettered notification can be requeued for replay, current status is {Status}.");

        Status = NotificationStatus.Queued;
    }
}
