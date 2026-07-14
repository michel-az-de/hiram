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

    public async Task UpsertAsync(TenantProviderConfig config, CancellationToken cancellationToken)
    {
        // Tracked read: an existing row is updated in place, a new one is inserted, so the (tenant, channel)
        // key is never deleted and reinserted.
        var existing = await _context.TenantProviderConfigs
            .FirstOrDefaultAsync(x => x.TenantId == config.TenantId && x.Channel == config.Channel, cancellationToken);

        if (existing is null)
            _context.TenantProviderConfigs.Add(config);
        else
            existing.Reconfigure(config.Provider, config.Settings, config.SecretProtected, config.UpdatedAtUtc);

        await _context.SaveChangesAsync(cancellationToken);
    }
}
