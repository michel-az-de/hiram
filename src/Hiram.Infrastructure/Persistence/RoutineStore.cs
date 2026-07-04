using Hiram.Application.Routines;
using Hiram.Domain.Routines;
using Microsoft.EntityFrameworkCore;

namespace Hiram.Infrastructure.Persistence;

public sealed class RoutineStore : IRoutineStore
{
    private readonly HiramDbContext _context;

    public RoutineStore(HiramDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(Routine routine, CancellationToken cancellationToken)
    {
        _context.Routines.Add(routine);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<Routine?> FindActiveAsync(Guid? tenantId, string eventType, CancellationToken cancellationToken) =>
        _context.Routines.FirstOrDefaultAsync(
            r => r.Active && r.EventType == eventType && r.TenantId == tenantId, cancellationToken);
}
