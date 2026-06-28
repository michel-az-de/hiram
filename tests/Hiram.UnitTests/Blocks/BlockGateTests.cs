using Hiram.Application.Blocks;
using Hiram.Domain.Blocks;
using Hiram.Domain.Notifications;

namespace Hiram.UnitTests.Blocks;

public class BlockGateTests
{
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeBlockStore(params Block[] active) : IBlockStore
    {
        public Task<IReadOnlyList<Block>> ActiveBlocksAsync(Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Block>>(active);

        public Task AddAsync(Block block, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> RemoveAsync(Guid tenantId, Guid id, DateTimeOffset removedAtUtc, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    [Fact]
    public async Task ActiveBlock_SuppressesEvent()
    {
        var block = new Block(Guid.NewGuid(), Tenant, NotificationChannel.Email, "provider outage", Now, expiresAtUtc: null);
        var gate = new BlockGate(new FakeBlockStore(block));

        var blocked = await gate.IsBlockedAsync(Tenant, NotificationChannel.Email, Now, CancellationToken.None);

        Assert.True(blocked);
    }

    [Fact]
    public async Task BlockOnAnotherChannel_DoesNotSuppress()
    {
        var block = new Block(Guid.NewGuid(), Tenant, NotificationChannel.Push, "push outage", Now, expiresAtUtc: null);
        var gate = new BlockGate(new FakeBlockStore(block));

        var blocked = await gate.IsBlockedAsync(Tenant, NotificationChannel.Email, Now, CancellationToken.None);

        Assert.False(blocked);
    }
}
