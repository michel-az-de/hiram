namespace Hiram.Application.Webhooks;

// Internal envelope on the outbox: carries tenant_id so the consumer can resolve the endpoints. The body
// posted to the tenant is the public event, without tenant_id.
public sealed record WebhookOutboxPayload(
    Guid TenantId,
    Guid NotificationId,
    string Channel,
    string Status,
    DateTimeOffset OccurredAt);
