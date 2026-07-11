using System.Text.Json;
using Hiram.Application.Abstractions;
using Hiram.Application.Metering;
using Hiram.Application.Notifications;
using Hiram.Domain.DeadLetters;
using Hiram.Domain.Metering;
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
        public CreditLedgerEntry? SavedLedger { get; private set; }
        public int SaveCalls { get; private set; }
        public Exception? ThrowOnSave { get; set; }

        public Task SaveAsync(NotificationRequest request, OutboxMessage outbox, CreditLedgerEntry ledgerEntry, CancellationToken cancellationToken)
        {
            SaveCalls++;
            if (ThrowOnSave is not null)
                throw ThrowOnSave;

            SavedRequest = request;
            SavedOutbox = outbox;
            SavedLedger = ledgerEntry;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeIdempotencyKeys : IIdempotencyKeys
    {
        public Guid? ClaimResult { get; set; }
        public int ClaimCalls { get; private set; }
        public int StoreCalls { get; private set; }
        public int ReleaseCalls { get; private set; }
        public Guid? LastStoredId { get; private set; }

        public Task<Guid?> TryClaimAsync(Guid tenantId, string key, Guid notificationId, CancellationToken cancellationToken)
        {
            ClaimCalls++;
            return Task.FromResult(ClaimResult);
        }

        public Task StoreAsync(Guid tenantId, string key, Guid notificationId, CancellationToken cancellationToken)
        {
            StoreCalls++;
            LastStoredId = notificationId;
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(Guid tenantId, string key, CancellationToken cancellationToken)
        {
            ReleaseCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeReader : INotificationReader
    {
        public Guid? ExistingId { get; set; }

        public Task<NotificationRequest?> FindAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<NotificationRequest?>(null);

        public Task<Guid?> FindIdByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(ExistingId);

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
        FakeIdempotencyKeys Idempotency,
        FakeReader Reader);

    private static Harness Build()
    {
        var store = new CapturingStore();
        var idempotency = new FakeIdempotencyKeys();
        var reader = new FakeReader();
        var calculator = new CreditCalculator(new CreditRates(new Dictionary<NotificationChannel, long>(), DefaultBase: 1, PerKilobyte: 1));
        var handler = new SubmitNotificationHandler(store, reader, idempotency, new FixedClock(FixedNow), calculator);
        return new Harness(handler, store, idempotency, reader);
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
    public async Task Submit_ChargesADebitForTheAcceptedNotification()
    {
        var harness = Build();

        var result = await harness.Handler.SubmitAsync(ValidCommand(), CancellationToken.None);

        Assert.NotNull(harness.Store.SavedLedger);
        Assert.True(harness.Store.SavedLedger!.Amount < 0);
        Assert.Equal(result.NotificationId, harness.Store.SavedLedger.NotificationId);
        Assert.Equal(DevTenant, harness.Store.SavedLedger.TenantId);
    }

    [Fact]
    public async Task Submit_WithKnownIdempotencyKey_DoesNotCharge()
    {
        var harness = Build();
        var original = Guid.NewGuid();
        harness.Idempotency.ClaimResult = original;
        harness.Reader.ExistingId = original;

        await harness.Handler.SubmitAsync(ValidCommand("evt-1"), CancellationToken.None);

        Assert.Null(harness.Store.SavedLedger);
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
    public async Task Submit_WithoutIdempotencyKey_NeverConsultsTheFastPath()
    {
        var harness = Build();

        await harness.Handler.SubmitAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal(0, harness.Idempotency.ClaimCalls);
    }

    [Fact]
    public async Task Submit_WithNewIdempotencyKey_AcceptsAndStoresTheKeyOnTheRequest()
    {
        var harness = Build();
        harness.Idempotency.ClaimResult = null;

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
        harness.Idempotency.ClaimResult = original;
        harness.Reader.ExistingId = original;

        var result = await harness.Handler.SubmitAsync(ValidCommand("evt-1"), CancellationToken.None);

        Assert.True(result.Replayed);
        Assert.Equal(original, result.NotificationId);
        Assert.Equal(0, harness.Store.SaveCalls);
    }

    [Fact]
    public async Task Submit_WhenClaimHasNoPersistedRow_SubmitsFreshInsteadOfReplaying()
    {
        var harness = Build();
        harness.Idempotency.ClaimResult = Guid.NewGuid(); // Redis points at a claim...
        harness.Reader.ExistingId = null;                 // ...whose owner never committed a row.

        var result = await harness.Handler.SubmitAsync(ValidCommand("evt-1"), CancellationToken.None);

        Assert.False(result.Replayed);
        Assert.Equal(1, harness.Store.SaveCalls);
        Assert.NotEqual(Guid.Empty, result.NotificationId);
    }

    [Fact]
    public async Task Submit_WhenDatabaseRejectsDuplicate_ReplaysOriginalAndRepairsTheCache()
    {
        var harness = Build();
        var original = Guid.NewGuid();
        harness.Idempotency.ClaimResult = null;
        harness.Store.ThrowOnSave = new DuplicateIdempotencyKeyException();
        harness.Reader.ExistingId = original;

        var result = await harness.Handler.SubmitAsync(ValidCommand("evt-1"), CancellationToken.None);

        Assert.True(result.Replayed);
        Assert.Equal(original, result.NotificationId);
        Assert.Equal(1, harness.Idempotency.StoreCalls);
        Assert.Equal(original, harness.Idempotency.LastStoredId);
    }

    [Fact]
    public async Task Submit_WhenSaveFailsForAnotherReason_ReleasesTheClaimAndRethrows()
    {
        var harness = Build();
        harness.Idempotency.ClaimResult = null;
        harness.Store.ThrowOnSave = new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Handler.SubmitAsync(ValidCommand("evt-1"), CancellationToken.None));

        Assert.Equal(1, harness.Idempotency.ReleaseCalls);
    }
}
