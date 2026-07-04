using System.Text.Json;
using Hiram.Application.Events;
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
        OutboxEventPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<OutboxEventPayload>(body.Span);
        }
        catch (JsonException ex)
        {
            throw new PoisonMessageException("Event message payload is not valid JSON.", ex);
        }

        if (payload is null)
            throw new PoisonMessageException("Event message payload is empty.");

        await using var scope = _scopeFactory.CreateAsyncScope();
        var fanout = scope.ServiceProvider.GetRequiredService<EventFanout>();
        await fanout.FanOutAsync(payload, cancellationToken);
    }
}
