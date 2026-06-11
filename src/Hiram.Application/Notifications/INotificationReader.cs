using Hiram.Domain.Notifications;

namespace Hiram.Application.Notifications;

public interface INotificationReader
{
    Task<NotificationRequest?> FindAsync(Guid id, CancellationToken cancellationToken);
}
