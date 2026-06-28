using Hiram.Domain.Blocks;

namespace Hiram.Application.Blocks;

public interface IBlockStore
{
    // Not removed and not expired, for this tenant or global.
    Task<IReadOnlyList<Block>> ActiveBlocksAsync(Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken);

    Task AddAsync(Block block, CancellationToken cancellationToken);

    // Scoped to the tenant: a tenant cannot remove another tenant's or a global block.
    Task<bool> RemoveAsync(Guid tenantId, Guid id, DateTimeOffset removedAtUtc, CancellationToken cancellationToken);
}
