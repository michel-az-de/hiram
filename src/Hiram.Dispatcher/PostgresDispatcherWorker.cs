namespace Hiram.Dispatcher;

public sealed class PostgresDispatcherWorker : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(250);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PostgresDispatcherWorker> _logger;

    public PostgresDispatcherWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<PostgresDispatcherWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var pump = scope.ServiceProvider.GetRequiredService<PostgresOutboxPump>();
                var processed = await pump.RunOnceAsync(stoppingToken);
                if (processed == 0)
                    await Task.Delay(IdleDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PostgreSQL dispatcher loop failed");
                await Task.Delay(IdleDelay, stoppingToken);
            }
        }
    }
}
