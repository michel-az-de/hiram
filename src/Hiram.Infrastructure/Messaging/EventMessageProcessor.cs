using System.Text.Json;
using Hiram.Application.Events;

namespace Hiram.Infrastructure.Messaging;

public sealed class EventMessageProcessor
{
    private readonly EventFanout _fanout;

    public EventMessageProcessor(EventFanout fanout)
    {
        _fanout = fanout;
    }

    public async Task ProcessAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
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

        await _fanout.FanOutAsync(payload, cancellationToken);
    }
}
