using Hiram.Domain.Events;
using Hiram.Domain.Outbox;

namespace Hiram.Application.Events;

public interface IEventStore
{
    // The event and its outbox row commit together or not at all: the founding invariant, for events.
    Task SaveAsync(NotificationEvent @event, OutboxMessage outbox, CancellationToken cancellationToken);

    Task<Guid?> FindIdByEventIdAsync(Guid tenantId, string eventId, CancellationToken cancellationToken);
}
