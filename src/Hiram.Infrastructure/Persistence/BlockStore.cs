using Hiram.Application.Blocks;
using Hiram.Domain.Blocks;
using Microsoft.EntityFrameworkCore;

namespace Hiram.Infrastructure.Persistence;

public sealed class BlockStore : IBlockStore
{
    private readonly HiramDbContext _context;

    public BlockStore(HiramDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Block>> ActiveBlocksAsync(Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await _context.Blocks
            .AsNoTracking()
            .Where(b => b.RemovedAtUtc == null
                && (b.ExpiresAtUtc == null || b.ExpiresAtUtc > now)
                && (b.TenantId == tenantId || b.TenantId == null))
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Block block, CancellationToken cancellationToken)
    {
        _context.Blocks.Add(block);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveAsync(Guid tenantId, Guid id, DateTimeOffset removedAtUtc, CancellationToken cancellationToken)
    {
        var block = await _context.Blocks.FirstOrDefaultAsync(b => b.Id == id && b.TenantId == tenantId, cancellationToken);
        if (block is null)
            return false;

        block.Remove(removedAtUtc);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
