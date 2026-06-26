namespace Hiram.Contracts;

public sealed record SubmitNotificationRequest(
    string Channel,
    string Recipient,
    string? Subject = null,
    string? Body = null,
    string? Template = null,
    IReadOnlyDictionary<string, object?>? Data = null);
