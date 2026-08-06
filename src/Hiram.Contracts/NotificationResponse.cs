namespace Hiram.Contracts;

public sealed record NotificationResponse(
    Guid Id,
    string Channel,
    string Recipient,
    string? Subject,
    string Status,
    DateTimeOffset CreatedAtUtc);
