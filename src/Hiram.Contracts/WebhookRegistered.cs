namespace Hiram.Contracts;

// Secret is the HMAC signing key, shown only once at registration and never returned again.
public sealed record WebhookRegistered(Guid Id, string Url, string Secret);
