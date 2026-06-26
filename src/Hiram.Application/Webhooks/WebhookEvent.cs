namespace Hiram.Application.Webhooks;

// The body posted to the tenant endpoint. The X-Hiram-Signature header is HMAC-SHA256 of these exact bytes.
public sealed record WebhookEvent(Guid NotificationId, string Channel, string Status, DateTimeOffset OccurredAt);
