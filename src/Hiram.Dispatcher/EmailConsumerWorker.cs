using Hiram.Infrastructure.Messaging;

namespace Hiram.Dispatcher;

public sealed class EmailConsumerWorker : RabbitConsumerWorker
{
    private readonly IServiceScopeFactory _scopeFactory;

    public EmailConsumerWorker(
        RabbitMqConnection connection,
        IServiceScopeFactory scopeFactory,
        ILogger<EmailConsumerWorker> logger)
        : base(connection, logger)
    {
        _scopeFactory = scopeFactory;
    }

    protected override string QueueName => HiramTopology.EmailQueue;
    protected override string ActivityName => "consume email";

    protected override async Task ProcessAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<EmailNotificationProcessor>();
        await processor.ProcessAsync(body, cancellationToken);
    }
}
