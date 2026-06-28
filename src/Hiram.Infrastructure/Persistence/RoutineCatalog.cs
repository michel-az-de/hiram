using Hiram.Application.Routines;
using Hiram.Domain.Routines;
using Microsoft.EntityFrameworkCore;

namespace Hiram.Infrastructure.Persistence;

public sealed class RoutineCatalog : IRoutineCatalog
{
    private readonly HiramDbContext _context;

    public RoutineCatalog(HiramDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Routine>> MatchAsync(Guid tenantId, string eventType, CancellationToken cancellationToken) =>
        await _context.Routines
            .Where(r => r.Active && r.EventType == eventType && (r.TenantId == tenantId || r.TenantId == null))
            .ToListAsync(cancellationToken);
}
