using Hiram.Infrastructure.Messaging;

namespace Hiram.Dispatcher;

// Consumes raw events off the event queue and hands each to the routine engine, which fans it out into
// channel messages on the existing queues. Mirrors EmailConsumerWorker: one scope per delivery, poison
// payloads park in the dead letter exchange, transient failures requeue.
public sealed class EventConsumerWorker : RabbitConsumerWorker
{
    private readonly IServiceScopeFactory _scopeFactory;

    public EventConsumerWorker(
        RabbitMqConnection connection,
        IServiceScopeFactory scopeFactory,
        ILogger<EventConsumerWorker> logger)
        : base(connection, logger)
    {
        _scopeFactory = scopeFactory;
    }

    protected override string QueueName => HiramTopology.EventQueue;
    protected override string ActivityName => "consume event";

    protected override async Task ProcessAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<EventMessageProcessor>();
        await processor.ProcessAsync(body, cancellationToken);
    }
}
