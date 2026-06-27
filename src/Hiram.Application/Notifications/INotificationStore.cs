using Hiram.Domain.Metering;
using Hiram.Domain.Notifications;
using Hiram.Domain.Outbox;

namespace Hiram.Application.Notifications;

public interface INotificationStore
{
    // The request, its outbox message and the credit debit must be persisted in a single transaction.
    // Losing one without the others is the exact failure this platform exists to prevent.
    Task SaveAsync(NotificationRequest request, OutboxMessage outbox, CreditLedgerEntry ledgerEntry, CancellationToken cancellationToken);
}
