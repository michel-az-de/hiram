using Hiram.Application.Abstractions;
using Hiram.Application.Events;
using Hiram.Domain.Events;
using Hiram.Domain.Outbox;

namespace Hiram.UnitTests.Events;

public class IngestEventHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);

    private sealed class CapturingStore : IEventStore
    {
        public NotificationEvent? SavedEvent { get; private set; }
        public OutboxMessage? SavedOutbox { get; private set; }
        public int SaveCalls { get; private set; }
        public Exception? ThrowOnSave { get; set; }
        public Guid? ExistingId { get; set; }

        public Task SaveAsync(NotificationEvent @event, OutboxMessage outbox, CancellationToken cancellationToken)
        {
            SaveCalls++;
            if (ThrowOnSave is not null)
                throw ThrowOnSave;

            SavedEvent = @event;
            SavedOutbox = outbox;
            return Task.CompletedTask;
        }

        public Task<Guid?> FindIdByEventIdAsync(Guid tenantId, string eventId, CancellationToken cancellationToken) =>
            Task.FromResult(ExistingId);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static IngestEventCommand Command(string eventId = "evt-1") =>
        new(Tenant, "produto_vencendo", eventId, 42,
            new EventPayload(null, "felipe@example.com", null, null, "America/Sao_Paulo", null));

    [Fact]
    public async Task IngestEvent_PersistsEventAndOutbox_Once()
    {
        var store = new CapturingStore();
        var handler = new IngestEventHandler(store, new FixedClock(FixedNow));

        var result = await handler.IngestAsync(Command(), CancellationToken.None);

        Assert.Equal(1, store.SaveCalls);
        Assert.False(result.Replayed);
        Assert.NotNull(store.SavedEvent);
        Assert.NotNull(store.SavedOutbox);
        Assert.Equal("event", store.SavedOutbox!.Type);
        Assert.Equal(Tenant, store.SavedEvent!.TenantId);
        Assert.Equal("produto_vencendo", store.SavedEvent.EventType);
        Assert.Equal(42, store.SavedEvent.EmissionSeq);
        Assert.Equal(result.Id, store.SavedEvent.Id);
    }

    [Fact]
    public async Task DuplicateEventId_DoesNotRefanout()
    {
        var original = Guid.NewGuid();
        var store = new CapturingStore { ThrowOnSave = new DuplicateEventException(), ExistingId = original };
        var handler = new IngestEventHandler(store, new FixedClock(FixedNow));

        var result = await handler.IngestAsync(Command(), CancellationToken.None);

        Assert.True(result.Replayed);
        Assert.Equal(original, result.Id);
        Assert.Equal(1, store.SaveCalls);
        Assert.Null(store.SavedOutbox);
    }
}
