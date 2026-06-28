namespace Hiram.Dispatcher;

public static class ConsumerDrain
{
    // Waits until no handler is in flight, or until the grace window elapses. Returns whether it
    // drained: false means the caller should close anyway and rely on at-least-once redelivery.
    public static async Task<bool> WaitAsync(
        Func<int> inFlightCount,
        TimeSpan grace,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + grace;
        while (inFlightCount() > 0)
        {
            if (DateTimeOffset.UtcNow >= deadline)
                return false;

            try
            {
                await Task.Delay(pollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return inFlightCount() == 0;
            }
        }

        return true;
    }
}
