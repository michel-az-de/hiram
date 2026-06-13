using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hiram.Application.Abstractions;
using Hiram.Application.Delivery;
using Hiram.Application.Notifications;
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

        var notification = await _context.NotificationRequests
            .FirstOrDefaultAsync(x => x.Id == payload.NotificationId, cancellationToken);
        if (notification is null)
        {
            _logger.LogWarning("Notification {NotificationId} not found while consuming email message", payload.NotificationId);
            return;
        }

        if (notification.Status is NotificationStatus.Sent or NotificationStatus.Failed)
        {
            // At-least-once redelivery of an already settled notification: nothing to send, let the worker ack.
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
                await RecordAttemptAsync(notification, attemptNumber, resolved.Provider.Name, attemptOutcome, stopwatch.Elapsed, startedAt, cancellationToken);
                return attemptOutcome;
            },
            cancellationToken);

        if (outcome is SendOutcome.Sent)
            notification.MarkSent();
        else
            notification.MarkFailed();

        await _context.SaveChangesAsync(cancellationToken);

        // The message is acked by the worker once this returns. There is deliberately no requeue: replay
        // and a dead letter queue arrive in F2, and requeue without a DLQ is an infinite loop.
    }

    private static async Task<SendOutcome> SendOnceAsync(
        ResolvedEmailProvider resolved,
        EmailMessage message,
        CancellationToken pipelineToken,
        CancellationToken outerToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(pipelineToken);
        timeout.CancelAfter(PerAttemptTimeout);

        try
        {
            return await resolved.Provider.SendAsync(message, resolved.Settings, timeout.Token);
        }
        catch (OperationCanceledException) when (!outerToken.IsCancellationRequested)
        {
            // The per-attempt timeout fired (not a shutdown); treat as transient so the pipeline retries.
            return new SendOutcome.TransientFailure($"Send exceeded the {PerAttemptTimeout.TotalSeconds:0}s attempt timeout.");
        }
    }

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
