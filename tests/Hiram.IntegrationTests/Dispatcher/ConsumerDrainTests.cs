using Hiram.Dispatcher;

namespace Hiram.IntegrationTests.Dispatcher;

// Pure drain-wait logic, Docker-free. The broker-level draining is wired into the consumer workers and
// exercised by the end-to-end suite in CI; here we lock the wait contract that gates the channel close.
public class ConsumerDrainTests
{
    [Fact]
    public async Task Drain_CompletesWhenInflightReachesZero()
    {
        var inFlight = 2;
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            Volatile.Write(ref inFlight, 0);
        });

        var drained = await ConsumerDrain.WaitAsync(
            () => Volatile.Read(ref inFlight),
            grace: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        Assert.True(drained);
    }

    [Fact]
    public async Task Drain_StopsAtGrace_WhenInflightStuck()
    {
        var drained = await ConsumerDrain.WaitAsync(
            () => 1,
            grace: TimeSpan.FromMilliseconds(100),
            pollInterval: TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        Assert.False(drained);
    }
}
