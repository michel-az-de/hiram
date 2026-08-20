using System.Diagnostics;
using System.Text.Json;
using Hiram.Application.Abstractions;
using Hiram.Domain.Notifications;
using Hiram.Domain.Outbox;

namespace Hiram.Application.Notifications;

public sealed class SubmitNotificationHandler : ISubmitNotification
{
    private readonly INotificationStore _store;
    private readonly INotificationReader _reader;
    private readonly IClock _clock;

    public SubmitNotificationHandler(
        INotificationStore store,
        INotificationReader reader,
        IClock clock)
    {
        _store = store;
        _reader = reader;
        _clock = clock;
    }

    public async Task<SubmitNotificationResult> SubmitAsync(SubmitNotificationCommand command, CancellationToken cancellationToken)
    {
        var key = command.IdempotencyKey;
        var notificationId = Guid.NewGuid();

        if (key is not null)
        {
            var persistedId = await _reader.FindIdByIdempotencyKeyAsync(command.TenantId, key, cancellationToken);
            if (persistedId is Guid replayId)
                return Replay(replayId);
        }

        var now = _clock.UtcNow;
        var request = new NotificationRequest(
            notificationId,
            command.TenantId,
            command.Channel,
            command.Recipient,
            command.Subject,
            command.Body,
            now,
            key);

        // Built from the request, not from the command: the request is what normalised the body, and a
        // payload carrying the raw text would deliver something other than what was persisted and counted.
        var payload = new OutboxNotificationPayload(
            notificationId,
            command.TenantId,
            command.Channel.ToString(),
            command.Recipient,
            command.Subject,
            request.Body);

        var outbox = new OutboxMessage(
            Guid.NewGuid(),
            command.TenantId,
            RoutingKeyFor(command.Channel),
            JsonSerializer.Serialize(payload),
            now,
            Activity.Current?.Id);

        try
        {
            await _store.SaveAsync(request, outbox, cancellationToken);
        }
        catch (DuplicateIdempotencyKeyException)
        {
            // Concurrent submits can both miss the initial read. The unique index arbitrates the race,
            // then the loser resolves to the row committed by the winner.
            var existingId = await _reader.FindIdByIdempotencyKeyAsync(command.TenantId, key!, cancellationToken);
            if (existingId is not Guid replayId)
                throw;

            return Replay(replayId);
        }

        return new SubmitNotificationResult(
            notificationId, NotificationStatus.Accepted, Segments: SegmentsFor(request));
    }

    private static int? SegmentsFor(NotificationRequest request) =>
        request.Channel is NotificationChannel.Sms ? SmsBody.From(request.Body).Segments : null;

    private static SubmitNotificationResult Replay(Guid notificationId) =>
        new(notificationId, NotificationStatus.Accepted, Replayed: true);

    private static string RoutingKeyFor(NotificationChannel channel) => channel switch
    {
        NotificationChannel.Email => "email",
        NotificationChannel.Push => "push",
        NotificationChannel.Sms => "sms",
        NotificationChannel.WhatsApp => "whatsapp",
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "No routing key configured for channel.")
    };
}
