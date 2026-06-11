using System.Text.Json;
using Hiram.Application.Abstractions;
using Hiram.Application.Notifications;
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

        public Task SaveAsync(NotificationRequest request, OutboxMessage outbox, CancellationToken cancellationToken)
        {
            SavedRequest = request;
            SavedOutbox = outbox;
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static (SubmitNotificationHandler Handler, CapturingStore Store) Build()
    {
        var store = new CapturingStore();
        var handler = new SubmitNotificationHandler(store, new FixedClock(FixedNow));
        return (handler, store);
    }

    private static SubmitNotificationCommand ValidCommand() =>
        new(DevTenant, NotificationChannel.Email, "felipe@example.com", "hello", "first slice");

    [Fact]
    public async Task Submit_ReturnsAcceptedNotificationId()
    {
        var (handler, _) = Build();

        var result = await handler.SubmitAsync(ValidCommand(), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.NotificationId);
        Assert.Equal(NotificationStatus.Accepted, result.Status);
    }

    [Fact]
    public async Task Submit_PersistsRequestAndOutboxInOneSaveSharingTenantAndTimestamp()
    {
        var (handler, store) = Build();

        var result = await handler.SubmitAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal(1, store.SaveCalls);
        Assert.NotNull(store.SavedRequest);
        Assert.NotNull(store.SavedOutbox);

        Assert.Equal(result.NotificationId, store.SavedRequest!.Id);
        Assert.Equal(NotificationStatus.Accepted, store.SavedRequest.Status);
        Assert.Equal(DevTenant, store.SavedRequest.TenantId);
        Assert.Equal(NotificationChannel.Email, store.SavedRequest.Channel);
        Assert.Equal("felipe@example.com", store.SavedRequest.Recipient);
        Assert.Equal(FixedNow, store.SavedRequest.CreatedAtUtc);

        Assert.Equal(DevTenant, store.SavedOutbox!.TenantId);
        Assert.Equal(FixedNow, store.SavedOutbox.CreatedAtUtc);
        Assert.Null(store.SavedOutbox.ProcessedAtUtc);
    }

    [Fact]
    public async Task Submit_SetsOutboxTypeToChannelRoutingKey()
    {
        var (handler, store) = Build();

        await handler.SubmitAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal("email", store.SavedOutbox!.Type);
    }

    [Fact]
    public async Task Submit_WritesNotificationIdIntoOutboxPayload()
    {
        var (handler, store) = Build();

        var result = await handler.SubmitAsync(ValidCommand(), CancellationToken.None);

        var payload = JsonSerializer.Deserialize<OutboxNotificationPayload>(store.SavedOutbox!.Payload);
        Assert.NotNull(payload);
        Assert.Equal(result.NotificationId, payload!.NotificationId);
        Assert.Equal("felipe@example.com", payload.Recipient);
        Assert.Equal("hello", payload.Subject);
    }
}
