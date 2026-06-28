using Hiram.Application.Messaging;

namespace Hiram.UnitTests.Messaging;

public class MessageDispatchGuardTests
{
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // Simulates the durable claim: the first claim of a key succeeds, later claims of the same key fail.
    private sealed class FakeClaims : IMessageClaimStore
    {
        private readonly HashSet<string> _seen = [];
        public Task<bool> TryClaimAsync(Guid tenantId, string messageKey, CancellationToken cancellationToken) =>
            Task.FromResult(_seen.Add(messageKey));
    }

    [Fact]
    public async Task Fanout_Replay_DoesNotResend()
    {
        var guard = new MessageDispatchGuard(new FakeClaims());
        var sends = 0;
        Task Send(CancellationToken _) { sends++; return Task.CompletedTask; }

        await guard.TryDispatchAsync(Tenant, "msg-key", Send, CancellationToken.None);
        await guard.TryDispatchAsync(Tenant, "msg-key", Send, CancellationToken.None);

        Assert.Equal(1, sends);
    }

    [Fact]
    public async Task Redelivery_DoesNotResend()
    {
        var guard = new MessageDispatchGuard(new FakeClaims());
        var sends = 0;
        Task Send(CancellationToken _) { sends++; return Task.CompletedTask; }

        // First delivery claims and sends; a broker redelivery of the same key must not send again.
        var first = await guard.TryDispatchAsync(Tenant, "msg-key", Send, CancellationToken.None);
        var second = await guard.TryDispatchAsync(Tenant, "msg-key", Send, CancellationToken.None);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(1, sends);
    }
}
