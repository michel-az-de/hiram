using System.Text.Json;
using Hiram.Application.Abstractions;
using Hiram.Domain.Notifications;
using Hiram.Domain.Outbox;

namespace Hiram.Application.Notifications;

public sealed class SubmitNotificationHandler : ISubmitNotification
{
    private readonly INotificationStore _store;
    private readonly IClock _clock;

    public SubmitNotificationHandler(INotificationStore store, IClock clock)
    {
        _store = store;
        _clock = clock;
    }

    public async Task<SubmitNotificationResult> SubmitAsync(SubmitNotificationCommand command, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var notificationId = Guid.NewGuid();

        var request = new NotificationRequest(
            notificationId,
            command.TenantId,
            command.Channel,
            command.Recipient,
            command.Subject,
            command.Body,
            now);

        var payload = new OutboxNotificationPayload(
            notificationId,
            command.TenantId,
            command.Channel.ToString(),
            command.Recipient,
            command.Subject,
            command.Body);

        var outbox = new OutboxMessage(
            Guid.NewGuid(),
            command.TenantId,
            RoutingKeyFor(command.Channel),
            JsonSerializer.Serialize(payload),
            now);

        await _store.SaveAsync(request, outbox, cancellationToken);

        return new SubmitNotificationResult(request.Id, request.Status);
    }

    private static string RoutingKeyFor(NotificationChannel channel) => channel switch
    {
        NotificationChannel.Email => "email",
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "No routing key configured for channel.")
    };
}
