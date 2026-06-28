using Hiram.Infrastructure.Messaging;

namespace Hiram.Dispatcher;

public sealed class PushConsumerWorker : RabbitConsumerWorker
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PushConsumerWorker(
        RabbitMqConnection connection,
        IServiceScopeFactory scopeFactory,
        ILogger<PushConsumerWorker> logger)
        : base(connection, logger)
    {
        _scopeFactory = scopeFactory;
    }

    protected override string QueueName => HiramTopology.PushQueue;
    protected override string ActivityName => "consume push";

    protected override async Task ProcessAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<PushNotificationProcessor>();
        await processor.ProcessAsync(body, cancellationToken);
    }
}
