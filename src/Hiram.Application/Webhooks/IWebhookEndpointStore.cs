using Hiram.Domain.Webhooks;

namespace Hiram.Application.Webhooks;

public interface IWebhookEndpointStore
{
    Task AddAsync(WebhookEndpoint endpoint, CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookEndpoint>> ListAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<bool> HasAnyAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
}
