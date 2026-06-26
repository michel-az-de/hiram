using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hiram.Application.Abstractions;
using Hiram.Application.Delivery;
using Hiram.Application.Notifications;
using Hiram.Domain.DeadLetters;
using Hiram.Domain.Notifications;
using Hiram.Domain.Tenants;
using Hiram.Infrastructure.Persistence;
using Hiram.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly;

namespace Hiram.Infrastructure.Messaging;

public sealed class EmailNotificationProcessor
{
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(10);

    private readonly HiramDbContext _context;
    private readonly EmailProviderResolver _resolver;
    private readonly ResiliencePipeline<SendOutcome> _pipeline;
    private readonly IClock _clock;
    private readonly ILogger<EmailNotificationProcessor> _logger;

    public EmailNotificationProcessor(
        HiramDbContext context,
        EmailProviderResolver resolver,
        ResiliencePipeline<SendOutcome> pipeline,
        IClock clock,
        ILogger<EmailNotificationProcessor> logger)
    {
        _context = context;
        _resolver = resolver;
        _pipeline = pipeline;
        _clock = clock;
        _logger = logger;
    }

    public async Task ProcessAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<OutboxNotificationPayload>(body.Span);
        if (payload is null)
        {
            _logger.LogWarning("Received an email message with an empty payload, skipping");
            return;
        }

        // Kept verbatim so a replay reproduces exactly what was attempted, not a re-render of the notification.
        var payloadJson = Encoding.UTF8.GetString(body.Span);

        var notification = await _context.NotificationRequests
            .FirstOrDefaultAsync(x => x.Id == payload.NotificationId, cancellationToken);
        if (notification is null)
        {
            _logger.LogWarning("Notification {NotificationId} not found while consuming email message", payload.NotificationId);
            return;
        }

        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["tenant_id"] = notification.TenantId,
            ["notification_id"] = notification.Id
        });

        if (notification.Status is NotificationStatus.Sent or NotificationStatus.Failed or NotificationStatus.DeadLettered)
        {
            // At-least-once redelivery of an already settled notification: nothing to send, let the worker ack.
            // Failed is kept here only to inert historical F1 rows; the live path now settles as dead lettered.
            return;
        }

        notification.MarkSending();
        await _context.SaveChangesAsync(cancellationToken);

        var resolved = await _resolver.ResolveAsync(notification.TenantId, cancellationToken);
        var message = new EmailMessage(notification.Recipient, notification.Subject, notification.Body);

        if (await IsShadowAsync(notification.TenantId, cancellationToken))
        {
            // Shadow mode processes everything up to the edge of the send, then records what would have
            // gone out without touching the provider, so a tenant can compare against its legacy system.
            await RecordShadowAttemptAsync(notification, resolved.Provider.Name, message, cancellationToken);
            notification.MarkSent();
            await _context.SaveChangesAsync(cancellationToken);
            HiramDiagnostics.NotificationsShadowed.Add(1);
            _logger.LogInformation("Email shadowed, would send via {Provider}", resolved.Provider.Name);
            return;
        }

        var attemptNumber = 0;
        var outcome = await _pipeline.ExecuteAsync(
            async token =>
            {
                attemptNumber++;
                var startedAt = _clock.UtcNow;
                var stopwatch = Stopwatch.StartNew();

                var attemptOutcome = await SendOnceAsync(resolved, message, token, cancellationToken);

                stopwatch.Stop();
                HiramDiagnostics.SendDuration.Record(
                    stopwatch.Elapsed.TotalMilliseconds,
                    new KeyValuePair<string, object?>("hiram.provider", resolved.Provider.Name));
                await RecordAttemptAsync(notification, attemptNumber, resolved.Provider.Name, attemptOutcome, stopwatch.Elapsed, startedAt, cancellationToken);
                return attemptOutcome;
            },
            cancellationToken);

        if (outcome is SendOutcome.Sent)
        {
            notification.MarkSent();
            HiramDiagnostics.NotificationsSent.Add(1);
        }
        else
        {
            var (reason, attemptCount) = DeadLetterReason(outcome, attemptNumber);
            _context.DeadLetterMessages.Add(new DeadLetterMessage(
                Guid.NewGuid(),
                notification.TenantId,
                notification.Id,
                notification.Channel,
                payloadJson,
                reason,
                attemptCount,
                _clock.UtcNow));

            notification.MarkDeadLettered();
            HiramDiagnostics.NotificationsFailed.Add(1);
            HiramDiagnostics.NotificationsDeadLettered.Add(1);
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Email delivery finished with status {Status}", notification.Status);

        // The message is acked by the worker once this returns. An exhausted or permanent failure now ends
        // dead lettered, recoverable through an explicit replay, so there is still deliberately no broker requeue.
    }

    private static (string Reason, int AttemptCount) DeadLetterReason(SendOutcome outcome, int attemptCount) => outcome switch
    {
        SendOutcome.PermanentFailure permanent => (Truncate($"permanent_failure:{permanent.Reason}"), attemptCount),
        SendOutcome.TransientFailure transient => (Truncate($"exhausted_transient:{transient.Reason}"), attemptCount),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome))
    };

    private static string Truncate(string reason) => reason.Length <= 256 ? reason : reason[..256];

    private static async Task<SendOutcome> SendOnceAsync(
        ResolvedEmailProvider resolved,
        EmailMessage message,
        CancellationToken pipelineToken,
        CancellationToken outerToken)
    {
        using var activity = HiramDiagnostics.Messaging.StartActivity("send email", ActivityKind.Client);
        activity?.SetTag("hiram.provider", resolved.Provider.Name);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(pipelineToken);
        timeout.CancelAfter(PerAttemptTimeout);

        SendOutcome outcome;
        try
        {
            outcome = await resolved.Provider.SendAsync(message, resolved.Settings, timeout.Token);
        }
        catch (OperationCanceledException) when (!outerToken.IsCancellationRequested)
        {
            // The per-attempt timeout fired (not a shutdown); treat as transient so the pipeline retries.
            outcome = new SendOutcome.TransientFailure($"Send exceeded the {PerAttemptTimeout.TotalSeconds:0}s attempt timeout.");
        }

        activity?.SetTag("hiram.outcome", OutcomeName(outcome));
        return outcome;
    }

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
        string provider,
        SendOutcome outcome,
        TimeSpan duration,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        var (deliveryOutcome, error) = Map(outcome);
        var attempt = new DeliveryAttempt(
            Guid.NewGuid(),
            notification.TenantId,
            notification.Id,
            attemptNumber,
            provider,
            deliveryOutcome,
            error,
            duration,
            createdAtUtc);

        _context.DeliveryAttempts.Add(attempt);
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

    private async Task RecordShadowAttemptAsync(
        NotificationRequest notification,
        string provider,
        EmailMessage message,
        CancellationToken cancellationToken)
    {
        var attempt = new DeliveryAttempt(
            Guid.NewGuid(),
            notification.TenantId,
            notification.Id,
            attemptNumber: 1,
            provider,
            DeliveryOutcome.ShadowWouldSend,
            error: null,
            duration: TimeSpan.Zero,
            _clock.UtcNow,
            shadowed: true,
            payloadHash: HashPayload(message));

        _context.DeliveryAttempts.Add(attempt);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static string HashPayload(EmailMessage message)
    {
        var canonical = $"{message.Recipient}\n{message.Subject}\n{message.Body}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
