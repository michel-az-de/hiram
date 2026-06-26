using Hiram.Domain.Push;

namespace Hiram.Application.Push;

public interface IPushSubscriptionStore
{
    Task AddAsync(PushSubscription subscription, CancellationToken cancellationToken);

    Task<PushSubscription?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<PushSubscription>> ListAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
}
