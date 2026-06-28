using Hiram.Domain.Notifications;

namespace Hiram.Application.Blocks;

public sealed class BlockGate
{
    private readonly IBlockStore _store;

    public BlockGate(IBlockStore store)
    {
        _store = store;
    }

    public async Task<bool> IsBlockedAsync(Guid tenantId, NotificationChannel channel, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var blocks = await _store.ActiveBlocksAsync(tenantId, now, cancellationToken);
        return blocks.Any(b => b.Channel is null || b.Channel == channel);
    }
}
