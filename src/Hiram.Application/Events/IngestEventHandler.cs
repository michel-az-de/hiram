using System.Diagnostics;
using System.Text.Json;
using Hiram.Application.Abstractions;
using Hiram.Domain.Events;
using Hiram.Domain.Outbox;

namespace Hiram.Application.Events;

public sealed class IngestEventHandler : IIngestEvent
{
    private const string OutboxType = "event";

    private readonly IEventStore _store;
    private readonly IClock _clock;

    public IngestEventHandler(IEventStore store, IClock clock)
    {
        _store = store;
        _clock = clock;
    }

    public async Task<IngestEventResult> IngestAsync(IngestEventCommand command, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var eventRowId = Guid.NewGuid();

        var @event = new NotificationEvent(
            eventRowId,
            command.TenantId,
            command.EventType,
            command.EventId,
            command.EmissionSeq,
            JsonSerializer.Serialize(command.Payload),
            now);

        var outboxPayload = new OutboxEventPayload(
            eventRowId, command.TenantId, command.EventType, command.EventId, command.EmissionSeq, command.Payload);

        var outbox = new OutboxMessage(
            Guid.NewGuid(),
            command.TenantId,
            OutboxType,
            JsonSerializer.Serialize(outboxPayload),
            now,
            Activity.Current?.Id);

        try
        {
            await _store.SaveAsync(@event, outbox, cancellationToken);
        }
        catch (DuplicateEventException)
        {
            // The event was already accepted; the unique index on (tenant_id, event_id) is the arbiter.
            // Return the original without fanning out again, idempotent.
            var existingId = await _store.FindIdByEventIdAsync(command.TenantId, command.EventId, cancellationToken);
            if (existingId is not Guid replayId)
                throw;

            return new IngestEventResult(replayId, EventStatus.Accepted, Replayed: true);
        }

        return new IngestEventResult(eventRowId, EventStatus.Accepted);
    }
}
