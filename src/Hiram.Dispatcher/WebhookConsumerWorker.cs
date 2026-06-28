using Hiram.Infrastructure.Messaging;

namespace Hiram.Dispatcher;

public sealed class WebhookConsumerWorker : RabbitConsumerWorker
{
    private readonly IServiceScopeFactory _scopeFactory;

    public WebhookConsumerWorker(
        RabbitMqConnection connection,
        IServiceScopeFactory scopeFactory,
        ILogger<WebhookConsumerWorker> logger)
        : base(connection, logger)
    {
        _scopeFactory = scopeFactory;
    }

    protected override string QueueName => HiramTopology.WebhookQueue;
    protected override string ActivityName => "consume webhook";

    protected override async Task ProcessAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<WebhookDeliveryProcessor>();
        await processor.ProcessAsync(body, cancellationToken);
    }
}
