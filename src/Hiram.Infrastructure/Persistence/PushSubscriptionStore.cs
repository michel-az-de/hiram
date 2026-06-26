using Hiram.Application.Push;
using Hiram.Domain.Push;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hiram.Infrastructure.Persistence;

public sealed class PushSubscriptionStore : IPushSubscriptionStore
{
    private const string EndpointIndex = "ux_push_subscriptions_tenant_endpoint";

    private readonly HiramDbContext _context;

    public PushSubscriptionStore(HiramDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(PushSubscription subscription, CancellationToken cancellationToken)
    {
        _context.PushSubscriptions.Add(subscription);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateEndpoint(ex))
        {
            throw new DuplicateSubscriptionException();
        }
    }

    public Task<PushSubscription?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        _context.PushSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PushSubscription>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await _context.PushSubscriptions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<bool> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _context.PushSubscriptions
            .Where(x => x.TenantId == tenantId && x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    private static bool IsDuplicateEndpoint(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: EndpointIndex
        };
}
