using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Hiram.Application.Abstractions;
using Hiram.Application.Metering;
using Hiram.Domain.Metering;
using Hiram.Domain.Notifications;
using Hiram.Domain.Outbox;

namespace Hiram.Application.Notifications;

public sealed class SubmitNotificationHandler : ISubmitNotification
{
    private readonly INotificationStore _store;
    private readonly INotificationReader _reader;
    private readonly IIdempotencyKeys _idempotency;
    private readonly IClock _clock;
    private readonly ICreditCalculator _calculator;

    public SubmitNotificationHandler(
        INotificationStore store,
        INotificationReader reader,
        IIdempotencyKeys idempotency,
        IClock clock,
        ICreditCalculator calculator)
    {
        _store = store;
        _reader = reader;
        _idempotency = idempotency;
        _clock = clock;
        _calculator = calculator;
    }

    public async Task<SubmitNotificationResult> SubmitAsync(SubmitNotificationCommand command, CancellationToken cancellationToken)
    {
        var key = command.IdempotencyKey;
        var notificationId = Guid.NewGuid();

        if (key is not null)
        {
            var claimed = await _idempotency.TryClaimAsync(command.TenantId, key, notificationId, cancellationToken);
            if (claimed is Guid replayId)
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
            now,
            Activity.Current?.Id);

        var payloadBytes = Encoding.UTF8.GetByteCount(command.Subject) + Encoding.UTF8.GetByteCount(command.Body);
        var cost = _calculator.Cost(command.Channel, payloadBytes);
        var ledgerEntry = CreditLedgerEntry.Debit(command.TenantId, notificationId, cost, "notification:accepted", now);

        try
        {
            await _store.SaveAsync(request, outbox, ledgerEntry, cancellationToken);
        }
        catch (DuplicateIdempotencyKeyException)
        {
            // The fast path missed (Redis cleared, expired or unavailable); the unique index is the durable
            // arbiter, so the conflict resolves to a replay of the original notification.
            var existingId = await _reader.FindIdByIdempotencyKeyAsync(command.TenantId, key!, cancellationToken);
            if (existingId is not Guid replayId)
                throw;

            await _idempotency.StoreAsync(command.TenantId, key!, replayId, cancellationToken);
            return Replay(replayId);
        }
        catch when (key is not null)
        {
            // Claimed the key but failed to persist for another reason; release it so a retry can proceed.
            await _idempotency.ReleaseAsync(command.TenantId, key, cancellationToken);
            throw;
        }

        return new SubmitNotificationResult(notificationId, NotificationStatus.Accepted);
    }

    private static SubmitNotificationResult Replay(Guid notificationId) =>
        new(notificationId, NotificationStatus.Accepted, Replayed: true);

    private static string RoutingKeyFor(NotificationChannel channel) => channel switch
    {
        NotificationChannel.Email => "email",
        NotificationChannel.Push => "push",
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "No routing key configured for channel.")
    };
}
