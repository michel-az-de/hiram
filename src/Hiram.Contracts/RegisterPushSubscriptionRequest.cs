namespace Hiram.Contracts;

public sealed record RegisterPushSubscriptionRequest(string Endpoint, string P256dh, string Auth);
