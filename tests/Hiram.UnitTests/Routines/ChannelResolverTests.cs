using Hiram.Application.Blocks;
using Hiram.Application.Consents;
using Hiram.Application.Routines;
using Hiram.Domain.Blocks;
using Hiram.Domain.Consents;
using Hiram.Domain.Notifications;
using Hiram.Domain.Routines;

namespace Hiram.UnitTests.Routines;

public class ChannelResolverTests
{
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid User = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly DateTimeOffset Now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeConsentStore(Dictionary<NotificationChannel, bool> optIn) : IConsentStore
    {
        public Task<Consent?> GetAsync(Guid tenantId, Guid userId, NotificationChannel channel, NotificationCategory category, CancellationToken cancellationToken) =>
            Task.FromResult(optIn.TryGetValue(channel, out var value)
                ? new Consent(Guid.NewGuid(), tenantId, userId, channel, category, value, Now)
                : null);

        public Task UpsertAsync(Consent consent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeBlockStore(params Block[] active) : IBlockStore
    {
        public Task<IReadOnlyList<Block>> ActiveBlocksAsync(Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Block>>(active);

        public Task AddAsync(Block block, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> RemoveAsync(Guid tenantId, Guid id, DateTimeOffset removedAtUtc, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private static ChannelResolver Resolver(FakeConsentStore consent, FakeBlockStore blocks) =>
        new(new ConsentPolicy(consent), new BlockGate(blocks));

    [Fact]
    public async Task ChannelResolver_KeepsAllowedChannelsInOrder()
    {
        var resolver = Resolver(new FakeConsentStore([]), new FakeBlockStore());

        var result = await resolver.ResolveAsync(
            Tenant, User, NotificationCategory.Operational,
            new[] { NotificationChannel.Email, NotificationChannel.Push }, Now, CancellationToken.None);

        Assert.Equal(new[] { NotificationChannel.Email, NotificationChannel.Push }, result);
    }

    [Fact]
    public async Task ChannelResolver_OrdersFallback_AppliesAllFilters()
    {
        // Email is blocked; Push is opted out. Both filters apply, so nothing survives.
        var consent = new FakeConsentStore(new Dictionary<NotificationChannel, bool> { [NotificationChannel.Push] = false });
        var blocks = new FakeBlockStore(new Block(Guid.NewGuid(), Tenant, NotificationChannel.Email, "outage", Now, null));
        var resolver = Resolver(consent, blocks);

        var result = await resolver.ResolveAsync(
            Tenant, User, NotificationCategory.Operational,
            new[] { NotificationChannel.Email, NotificationChannel.Push }, Now, CancellationToken.None);

        Assert.Empty(result);
    }
}
