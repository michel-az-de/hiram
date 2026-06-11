using Hiram.Infrastructure.Messaging;

namespace Hiram.Dispatcher;

public sealed class OutboxRelayWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxRelayWorker> _logger;

    public OutboxRelayWorker(IServiceScopeFactory scopeFactory, ILogger<OutboxRelayWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var relay = scope.ServiceProvider.GetRequiredService<OutboxRelay>();
                await relay.RelayPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Keep the worker alive across transient database or broker failures; the locked rows
                // stay pending and the next tick retries them.
                _logger.LogError(ex, "Outbox relay cycle failed, retrying on next tick");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
