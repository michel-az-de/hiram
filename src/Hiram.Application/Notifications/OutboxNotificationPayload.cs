namespace Hiram.Application.Notifications;

public sealed record OutboxNotificationPayload(
    Guid NotificationId,
    Guid TenantId,
    string Channel,
    string Recipient,
    string Subject,
    string Body);
