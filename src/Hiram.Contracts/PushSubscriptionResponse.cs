namespace Hiram.Contracts;

public sealed record PushSubscriptionResponse(Guid Id, string Endpoint, DateTimeOffset CreatedAtUtc);
