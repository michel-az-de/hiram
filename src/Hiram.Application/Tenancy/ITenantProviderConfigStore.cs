using Hiram.Domain.Notifications;
using Hiram.Domain.Tenants;

namespace Hiram.Application.Tenancy;

public interface ITenantProviderConfigStore
{
    Task<TenantProviderConfig?> FindAsync(Guid tenantId, NotificationChannel channel, CancellationToken cancellationToken);
}
