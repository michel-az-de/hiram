using System.Diagnostics;
using Hiram.Application.Abstractions;
using Hiram.Application.Outbox;
using Hiram.Infrastructure.Messaging;
using Hiram.Infrastructure.Telemetry;

namespace Hiram.Api.Workers;

public sealed class PostgresOutboxPump
{
    // One message per scoped pump keeps the lease budget tied to actual processing time. Claiming a
    // larger batch and handling it sequentially would let later leases age while earlier providers run.
    private const int BatchSize = 1;
    private const int MaximumAttempts = 5;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    private readonly IOutboxQueue _queue;
    private readonly OutboxMessageDispatcher _dispatcher;
    private readonly IClock _clock;
    private readonly ILogger<PostgresOutboxPump> _logger;

    public PostgresOutboxPump(
        IOutboxQueue queue,
        OutboxMessageDispatcher dispatcher,
        IClock clock,
        ILogger<PostgresOutboxPump> logger)
    {
        _queue = queue;
        _dispatcher = dispatcher;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var claimed = await _queue.ClaimAsync(BatchSize, _clock.UtcNow, LeaseDuration, cancellationToken);

        foreach (var message in claimed)
            await ProcessAsync(message, cancellationToken);

        return claimed.Count;
    }

    private async Task ProcessAsync(OutboxLease message, CancellationToken cancellationToken)
    {
        using var activity = HiramDiagnostics.Messaging.StartActivity(
            $"consume {message.Type}",
            ActivityKind.Consumer,
            ParseContext(message.TraceParent));

        try
        {
            await _dispatcher.DispatchAsync(message, cancellationToken);
            var completed = await _queue.CompleteAsync(
                message.Id, message.LeaseUntil, _clock.UtcNow, cancellationToken);
            if (completed)
                HiramDiagnostics.OutboxDispatched.Add(1);
            else
                _logger.LogWarning("Lost lease before completing outbox message {OutboxMessageId}", message.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Leave the lease untouched. Shutdown waits for this call; if cancellation interrupts it,
            // another worker can recover the message only after the lease expires.
            throw;
        }
        catch (PoisonMessageException ex)
        {
            _logger.LogWarning(ex, "Dead-lettering poison outbox message {OutboxMessageId}", message.Id);
            await DeadLetterAsync(message, ex.Message, cancellationToken);
            HiramDiagnostics.Poisoned.Add(1);
        }
        catch (Exception ex)
        {
            var now = _clock.UtcNow;
            if (message.AttemptCount >= MaximumAttempts)
            {
                _logger.LogError(ex, "Dead-lettering exhausted outbox message {OutboxMessageId}", message.Id);
                await DeadLetterAsync(message, ex.Message, cancellationToken);
                return;
            }

            var availableAt = now.Add(RetryDelay(message.AttemptCount));
            var scheduled = await _queue.ScheduleRetryAsync(
                message.Id, message.LeaseUntil, now, availableAt, ex.Message, cancellationToken);
            if (scheduled)
            {
                _logger.LogWarning(
                    ex,
                    "Scheduled retry {AttemptCount} for outbox message {OutboxMessageId} at {AvailableAt}",
                    message.AttemptCount,
                    message.Id,
                    availableAt);
            }
            else
            {
                _logger.LogWarning(
                    ex,
                    "Lost lease before scheduling retry for outbox message {OutboxMessageId}",
                    message.Id);
            }
        }
    }

    private async Task DeadLetterAsync(OutboxLease message, string error, CancellationToken cancellationToken)
    {
        var deadLettered = await _queue.DeadLetterAsync(
            message.Id, message.LeaseUntil, _clock.UtcNow, error, cancellationToken);
        if (!deadLettered)
            _logger.LogWarning("Lost lease before dead-lettering outbox message {OutboxMessageId}", message.Id);
    }

    private static TimeSpan RetryDelay(int attemptCount) =>
        TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attemptCount)));

    private static ActivityContext ParseContext(string? traceParent) =>
        !string.IsNullOrEmpty(traceParent) && ActivityContext.TryParse(traceParent, null, out var context)
            ? context
            : default;
}
