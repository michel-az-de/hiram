using System.Text.Json;
using Hiram.Application.Abstractions;
using Hiram.Application.Notifications;
using Hiram.Domain.DeadLetters;
using Hiram.Domain.Notifications;
using Hiram.Domain.Outbox;

namespace Hiram.UnitTests.Notifications;

public class SubmitNotificationHandlerTests
{
    private static readonly Guid DevTenant = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class CapturingStore : INotificationStore
    {
        public NotificationRequest? SavedRequest { get; private set; }
        public OutboxMessage? SavedOutbox { get; private set; }
        public int SaveCalls { get; private set; }
        public Exception? ThrowOnSave { get; set; }

        public Task SaveAsync(NotificationRequest request, OutboxMessage outbox, CancellationToken cancellationToken)
        {
            SaveCalls++;
            if (ThrowOnSave is not null)
                throw ThrowOnSave;

            SavedRequest = request;
            SavedOutbox = outbox;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeReader : INotificationReader
    {
        public Guid? ExistingId { get; set; }
        public Queue<Guid?> IdempotencyResults { get; } = new();
        public int IdempotencyLookups { get; private set; }

        public Task<NotificationRequest?> FindAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<NotificationRequest?>(null);

        public Task<Guid?> FindIdByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken cancellationToken)
        {
            IdempotencyLookups++;
            return Task.FromResult(IdempotencyResults.TryDequeue(out var result) ? result : ExistingId);
        }

        public Task<IReadOnlyList<NotificationRequest>> QueryAsync(NotificationQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NotificationRequest>>([]);

        public Task<IReadOnlyList<DeliveryAttempt>> AttemptsAsync(Guid tenantId, Guid notificationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DeliveryAttempt>>([]);

        public Task<DeadLetterMessage?> LatestDeadLetterAsync(Guid tenantId, Guid notificationId, CancellationToken cancellationToken) =>
            Task.FromResult<DeadLetterMessage?>(null);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed record Harness(
        SubmitNotificationHandler Handler,
        CapturingStore Store,
        FakeReader Reader);

    private static Harness Build()
    {
        var store = new CapturingStore();
        var reader = new FakeReader();
        var handler = new SubmitNotificationHandler(store, reader, new FixedClock(FixedNow));
        return new Harness(handler, store, reader);
    }

    private static SubmitNotificationCommand ValidCommand(string? idempotencyKey = null) =>
        new(DevTenant, NotificationChannel.Email, "felipe@example.com", "hello", "first slice", idempotencyKey);

    [Fact]
    public async Task Submit_ReturnsAcceptedNotificationId()
    {
        var harness = Build();

        var result = await harness.Handler.SubmitAsync(ValidCommand(), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.NotificationId);
        Assert.Equal(NotificationStatus.Accepted, result.Status);
        Assert.False(result.Replayed);
    }

    [Fact]
    public async Task Submit_PersistsRequestAndOutboxInOneSaveSharingTenantAndTimestamp()
    {
        var harness = Build();

        var result = await harness.Handler.SubmitAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal(1, harness.Store.SaveCalls);
        Assert.NotNull(harness.Store.SavedRequest);
        Assert.NotNull(harness.Store.SavedOutbox);

        Assert.Equal(result.NotificationId, harness.Store.SavedRequest!.Id);
        Assert.Equal(NotificationStatus.Accepted, harness.Store.SavedRequest.Status);
        Assert.Equal(DevTenant, harness.Store.SavedRequest.TenantId);
        Assert.Equal(NotificationChannel.Email, harness.Store.SavedRequest.Channel);
        Assert.Equal("felipe@example.com", harness.Store.SavedRequest.Recipient);
        Assert.Equal(FixedNow, harness.Store.SavedRequest.CreatedAtUtc);

        Assert.Equal(DevTenant, harness.Store.SavedOutbox!.TenantId);
        Assert.Equal(FixedNow, harness.Store.SavedOutbox.CreatedAtUtc);
        Assert.Null(harness.Store.SavedOutbox.ProcessedAtUtc);
    }

    [Fact]
    public async Task Submit_SetsOutboxTypeToChannelRoutingKey()
    {
        var harness = Build();

        await harness.Handler.SubmitAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal("email", harness.Store.SavedOutbox!.Type);
    }

    [Fact]
    public async Task Submit_WritesNotificationIdIntoOutboxPayload()
    {
        var harness = Build();

        var result = await harness.Handler.SubmitAsync(ValidCommand(), CancellationToken.None);

        var payload = JsonSerializer.Deserialize<OutboxNotificationPayload>(harness.Store.SavedOutbox!.Payload);
        Assert.NotNull(payload);
        Assert.Equal(result.NotificationId, payload!.NotificationId);
        Assert.Equal("felipe@example.com", payload.Recipient);
        Assert.Equal("hello", payload.Subject);
    }

    [Fact]
    public async Task Submit_WithoutIdempotencyKey_DoesNotQueryForAnExistingRequest()
    {
        var harness = Build();

        await harness.Handler.SubmitAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal(0, harness.Reader.IdempotencyLookups);
    }

    [Fact]
    public async Task Submit_WithNewIdempotencyKey_AcceptsAndStoresTheKeyOnTheRequest()
    {
        var harness = Build();

        var result = await harness.Handler.SubmitAsync(ValidCommand("evt-1"), CancellationToken.None);

        Assert.False(result.Replayed);
        Assert.Equal(1, harness.Store.SaveCalls);
        Assert.Equal("evt-1", harness.Store.SavedRequest!.IdempotencyKey);
    }

    [Fact]
    public async Task Submit_WithKnownIdempotencyKey_ReplaysOriginalWithoutSaving()
    {
        var harness = Build();
        var original = Guid.NewGuid();
        harness.Reader.ExistingId = original;

        var result = await harness.Handler.SubmitAsync(ValidCommand("evt-1"), CancellationToken.None);

        Assert.True(result.Replayed);
        Assert.Equal(original, result.NotificationId);
        Assert.Equal(0, harness.Store.SaveCalls);
    }

    [Fact]
    public async Task Submit_WhenDatabaseRejectsConcurrentDuplicate_ReplaysOriginal()
    {
        var harness = Build();
        var original = Guid.NewGuid();
        harness.Store.ThrowOnSave = new DuplicateIdempotencyKeyException();
        harness.Reader.IdempotencyResults.Enqueue(null);
        harness.Reader.IdempotencyResults.Enqueue(original);

        var result = await harness.Handler.SubmitAsync(ValidCommand("evt-1"), CancellationToken.None);

        Assert.True(result.Replayed);
        Assert.Equal(original, result.NotificationId);
        Assert.Equal(2, harness.Reader.IdempotencyLookups);
    }

    [Fact]
    public async Task Submit_WhenSaveFailsForAnotherReason_Rethrows()
    {
        var harness = Build();
        harness.Store.ThrowOnSave = new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Handler.SubmitAsync(ValidCommand("evt-1"), CancellationToken.None));
    }

    [Fact]
    public async Task Submit_RoutesWhatsAppToItsOwnOutboxType()
    {
        var harness = Build();
        var command = new SubmitNotificationCommand(
            DevTenant, NotificationChannel.WhatsApp, "+5511999990000", null, "body", null);

        await harness.Handler.SubmitAsync(command, CancellationToken.None);

        // The routing key is what the dispatcher switches on, so a channel that lands under the wrong
        // type is poison rather than a delivery.
        Assert.Equal("whatsapp", harness.Store.SavedOutbox!.Type);
        Assert.Equal(NotificationChannel.WhatsApp, harness.Store.SavedRequest!.Channel);
        Assert.Null(harness.Store.SavedRequest.Subject);
    }
}
