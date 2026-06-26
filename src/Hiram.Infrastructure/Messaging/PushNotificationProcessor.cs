using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hiram.Application.Abstractions;
using Hiram.Application.Delivery;
using Hiram.Application.Notifications;
using Hiram.Application.Push;
using Hiram.Domain.DeadLetters;
using Hiram.Domain.Notifications;
using Hiram.Domain.Push;
using Hiram.Domain.Tenants;
using Hiram.Infrastructure.Persistence;
using Hiram.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly;

namespace Hiram.Infrastructure.Messaging;

public sealed class PushNotificationProcessor
{
    private const string Provider = "webpush";
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(10);

    private readonly HiramDbContext _context;
    private readonly IPushSender _sender;
    private readonly ResiliencePipeline<SendOutcome> _pipeline;
    private readonly IClock _clock;
    private readonly ILogger<PushNotificationProcessor> _logger;

    public PushNotificationProcessor(
        HiramDbContext context,
        IPushSender sender,
        ResiliencePipeline<SendOutcome> pipeline,
        IClock clock,
        ILogger<PushNotificationProcessor> logger)
    {
        _context = context;
        _sender = sender;
        _pipeline = pipeline;
        _clock = clock;
        _logger = logger;
    }

    public async Task ProcessAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        OutboxNotificationPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<OutboxNotificationPayload>(body.Span);
        }
        catch (JsonException ex)
        {
            throw new PoisonMessageException("Push message payload is not valid JSON.", ex);
        }

        if (payload is null)
            throw new PoisonMessageException("Push message payload is empty.");

        var payloadJson = Encoding.UTF8.GetString(body.Span);

        var notification = await _context.NotificationRequests
            .FirstOrDefaultAsync(x => x.Id == payload.NotificationId, cancellationToken);
        if (notification is null)
            throw new PoisonMessageException($"Notification {payload.NotificationId} was not found while consuming a push message.");

        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["tenant_id"] = notification.TenantId,
            ["notification_id"] = notification.Id
        });

        if (notification.Status is NotificationStatus.Sent or NotificationStatus.Failed or NotificationStatus.DeadLettered)
            return;

        notification.MarkSending();
        await _context.SaveChangesAsync(cancellationToken);

        if (await IsShadowAsync(notification.TenantId, cancellationToken))
        {
            await RecordShadowAttemptAsync(notification, cancellationToken);
            notification.MarkSent();
            await _context.SaveChangesAsync(cancellationToken);
            HiramDiagnostics.NotificationsShadowed.Add(1);
            _logger.LogInformation("Push shadowed, would send to subscription {Recipient}", notification.Recipient);
            return;
        }

        var subscription = await ResolveSubscriptionAsync(notification, cancellationToken);
        var pushPayload = JsonSerializer.Serialize(new { title = notification.Subject, body = notification.Body });

        int attemptCount;
        SendOutcome outcome;
        if (subscription is null)
        {
            // The subscription id does not resolve for this tenant: a permanent delivery failure, recorded once.
            attemptCount = 1;
            outcome = new SendOutcome.PermanentFailure("subscription_not_found");
            await RecordAttemptAsync(notification, attemptCount, outcome, TimeSpan.Zero, _clock.UtcNow, cancellationToken);
        }
        else
        {
            var attempts = 0;
            outcome = await _pipeline.ExecuteAsync(
                async token =>
                {
                    attempts++;
                    var startedAt = _clock.UtcNow;
                    var stopwatch = Stopwatch.StartNew();

                    var attemptOutcome = await SendOnceAsync(subscription, pushPayload, token, cancellationToken);

                    stopwatch.Stop();
                    HiramDiagnostics.SendDuration.Record(
                        stopwatch.Elapsed.TotalMilliseconds,
                        new KeyValuePair<string, object?>("hiram.provider", Provider));
                    await RecordAttemptAsync(notification, attempts, attemptOutcome, stopwatch.Elapsed, startedAt, cancellationToken);
                    return attemptOutcome;
                },
                cancellationToken);
            attemptCount = attempts;
        }

        if (outcome is SendOutcome.Sent)
        {
            notification.MarkSent();
            HiramDiagnostics.NotificationsSent.Add(1);
        }
        else
        {
            var (reason, count) = DeadLetterReason(outcome, attemptCount);
            _context.DeadLetterMessages.Add(new DeadLetterMessage(
                Guid.NewGuid(),
                notification.TenantId,
                notification.Id,
                notification.Channel,
                payloadJson,
                reason,
                count,
                _clock.UtcNow));

            notification.MarkDeadLettered();
            HiramDiagnostics.NotificationsFailed.Add(1);
            HiramDiagnostics.NotificationsDeadLettered.Add(1);
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Push delivery finished with status {Status}", notification.Status);
    }

    private async Task<PushSubscription?> ResolveSubscriptionAsync(NotificationRequest notification, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(notification.Recipient, out var subscriptionId))
            return null;

        return await _context.PushSubscriptions
            .FirstOrDefaultAsync(s => s.TenantId == notification.TenantId && s.Id == subscriptionId, cancellationToken);
    }

    private async Task<SendOutcome> SendOnceAsync(
        PushSubscription subscription, string payload, CancellationToken pipelineToken, CancellationToken outerToken)
    {
        using var activity = HiramDiagnostics.Messaging.StartActivity("send push", ActivityKind.Client);
        activity?.SetTag("hiram.provider", Provider);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(pipelineToken);
        timeout.CancelAfter(PerAttemptTimeout);

        SendOutcome outcome;
        try
        {
            outcome = await _sender.SendAsync(subscription, payload, timeout.Token);
        }
        catch (OperationCanceledException) when (!outerToken.IsCancellationRequested)
        {
            outcome = new SendOutcome.TransientFailure($"Send exceeded the {PerAttemptTimeout.TotalSeconds:0}s attempt timeout.");
        }

        activity?.SetTag("hiram.outcome", OutcomeName(outcome));
        return outcome;
    }

    private static (string Reason, int AttemptCount) DeadLetterReason(SendOutcome outcome, int attemptCount) => outcome switch
    {
        SendOutcome.PermanentFailure permanent => (Truncate($"permanent_failure:{permanent.Reason}"), attemptCount),
        SendOutcome.TransientFailure transient => (Truncate($"exhausted_transient:{transient.Reason}"), attemptCount),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome))
    };

    private static string Truncate(string reason) => reason.Length <= 256 ? reason : reason[..256];

    private static string OutcomeName(SendOutcome outcome) => outcome switch
    {
        SendOutcome.Sent => "sent",
        SendOutcome.TransientFailure => "transient_failure",
        SendOutcome.PermanentFailure => "permanent_failure",
        _ => "unknown"
    };

    private async Task RecordAttemptAsync(
        NotificationRequest notification,
        int attemptNumber,
        SendOutcome outcome,
        TimeSpan duration,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        var (deliveryOutcome, error) = Map(outcome);
        _context.DeliveryAttempts.Add(new DeliveryAttempt(
            Guid.NewGuid(),
            notification.TenantId,
            notification.Id,
            attemptNumber,
            Provider,
            deliveryOutcome,
            error,
            duration,
            createdAtUtc));

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static (DeliveryOutcome Outcome, string? Error) Map(SendOutcome outcome) => outcome switch
    {
        SendOutcome.Sent => (DeliveryOutcome.Sent, null),
        SendOutcome.TransientFailure transient => (DeliveryOutcome.TransientFailure, transient.Reason),
        SendOutcome.PermanentFailure permanent => (DeliveryOutcome.PermanentFailure, permanent.Reason),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome))
    };

    private async Task<bool> IsShadowAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var deliveryMode = await _context.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => (DeliveryMode?)t.DeliveryMode)
            .FirstOrDefaultAsync(cancellationToken);

        return deliveryMode == DeliveryMode.Shadow;
    }

    private async Task RecordShadowAttemptAsync(NotificationRequest notification, CancellationToken cancellationToken)
    {
        _context.DeliveryAttempts.Add(new DeliveryAttempt(
            Guid.NewGuid(),
            notification.TenantId,
            notification.Id,
            attemptNumber: 1,
            Provider,
            DeliveryOutcome.ShadowWouldSend,
            error: null,
            duration: TimeSpan.Zero,
            _clock.UtcNow,
            shadowed: true,
            payloadHash: HashPayload(notification)));

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static string HashPayload(NotificationRequest notification)
    {
        var canonical = $"{notification.Recipient}\n{notification.Subject}\n{notification.Body}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
