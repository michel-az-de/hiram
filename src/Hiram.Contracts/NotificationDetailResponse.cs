namespace Hiram.Contracts;

public sealed record NotificationDetailResponse(
    Guid Id,
    string Channel,
    string Recipient,
    string Subject,
    string Status,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<DeliveryAttemptView> Attempts,
    DeadLetterView? DeadLetter = null);
