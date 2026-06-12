using Hiram.Domain.Notifications;

namespace Hiram.Application.Notifications;

public sealed record SubmitNotificationCommand(
    Guid TenantId,
    NotificationChannel Channel,
    string Recipient,
    string Subject,
    string Body,
    string? IdempotencyKey = null);
