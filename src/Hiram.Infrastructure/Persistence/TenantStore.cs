using Hiram.Application.Tenancy;
using Hiram.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Hiram.Infrastructure.Persistence;

public sealed class TenantStore : ITenantStore
{
    private readonly HiramDbContext _context;

    public TenantStore(HiramDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        _context.Tenants.AnyAsync(x => x.Id == id, cancellationToken);
}
