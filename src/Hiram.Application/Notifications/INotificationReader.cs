using Hiram.Domain.Notifications;

namespace Hiram.Application.Notifications;

public interface INotificationReader
{
    Task<NotificationRequest?> FindAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task<Guid?> FindIdByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationRequest>> QueryAsync(NotificationQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<DeliveryAttempt>> AttemptsAsync(Guid tenantId, Guid notificationId, CancellationToken cancellationToken);
}
