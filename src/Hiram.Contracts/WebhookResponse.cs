namespace Hiram.Contracts;

public sealed record WebhookResponse(Guid Id, string Url, DateTimeOffset CreatedAtUtc);
