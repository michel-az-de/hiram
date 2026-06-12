using Hiram.Domain.Notifications;

namespace Hiram.Application.Notifications;

public interface INotificationReader
{
    Task<NotificationRequest?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<Guid?> FindIdByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken cancellationToken);
}
