using Hiram.Application.Tenancy;
using Hiram.Domain.Notifications;
using Hiram.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Hiram.Infrastructure.Persistence;

public sealed class TenantProviderConfigStore : ITenantProviderConfigStore
{
    private readonly HiramDbContext _context;

    public TenantProviderConfigStore(HiramDbContext context)
    {
        _context = context;
    }

    public Task<TenantProviderConfig?> FindAsync(Guid tenantId, NotificationChannel channel, CancellationToken cancellationToken) =>
        _context.TenantProviderConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Channel == channel, cancellationToken);
}
