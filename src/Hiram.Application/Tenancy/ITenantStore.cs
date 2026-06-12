using Hiram.Domain.Tenants;

namespace Hiram.Application.Tenancy;

public interface ITenantStore
{
    Task AddAsync(Tenant tenant, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);
}
