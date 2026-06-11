using System.Diagnostics;
using Hiram.Application.Abstractions;
using Hiram.Infrastructure.Persistence;
using Hiram.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Hiram.Infrastructure.Messaging;

public sealed class OutboxRelay
{
    // Lock the oldest unprocessed rows and skip rows another worker already holds, so several
    // relays can share the load without double publishing within a transaction.
    private const string LockBatchSql =
        "SELECT * FROM notifications.outbox_messages " +
        "WHERE processed_at_utc IS NULL ORDER BY created_at_utc FOR UPDATE SKIP LOCKED LIMIT 50";

    private readonly HiramDbContext _context;
    private readonly RabbitMqConnection _connection;
    private readonly IClock _clock;
    private readonly ILogger<OutboxRelay> _logger;

    public OutboxRelay(HiramDbContext context, RabbitMqConnection connection, IClock clock, ILogger<OutboxRelay> logger)
    {
        _context = context;
        _connection = connection;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> RelayPendingAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var batch = await _context.OutboxMessages
            .FromSqlRaw(LockBatchSql)
            .ToListAsync(cancellationToken);

        if (batch.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        var connection = await _connection.GetConnectionAsync(cancellationToken);
        var options = new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);
        await using var channel = await connection.CreateChannelAsync(options, cancellationToken);

        foreach (var message in batch)
        {
            using var activity = HiramDiagnostics.Messaging.StartActivity(
                "publish email", ActivityKind.Producer, ParseContext(message.TraceParent));

            var headers = new Dictionary<string, object?>();
            var traceParent = activity?.Id ?? message.TraceParent;
            if (traceParent is not null)
                headers["traceparent"] = traceParent;

            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                Headers = headers
            };
            var body = System.Text.Encoding.UTF8.GetBytes(message.Payload);

            // Awaiting the publish waits for the broker to confirm the message before we mark the row
            // processed. A crash before the commit leaves the row pending and it is republished: at-least-once.
            await channel.BasicPublishAsync(HiramTopology.Exchange, message.Type, mandatory: false, properties, body, cancellationToken);

            message.MarkProcessed(_clock.UtcNow);
            HiramDiagnostics.OutboxDispatched.Add(1);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Relayed {Count} outbox message(s) to RabbitMQ", batch.Count);
        return batch.Count;
    }

    private static ActivityContext ParseContext(string? traceParent) =>
        !string.IsNullOrEmpty(traceParent) && ActivityContext.TryParse(traceParent, null, out var context)
            ? context
            : default;
}
