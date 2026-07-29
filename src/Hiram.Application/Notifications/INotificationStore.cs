using Hiram.Domain.Notifications;
using Hiram.Domain.Outbox;

namespace Hiram.Application.Notifications;

public interface INotificationStore
{
    // The request and its outbox message must be persisted in a single transaction. Losing one without
    // the other is the exact failure this platform exists to prevent.
    Task SaveAsync(NotificationRequest request, OutboxMessage outbox, CancellationToken cancellationToken);
}
