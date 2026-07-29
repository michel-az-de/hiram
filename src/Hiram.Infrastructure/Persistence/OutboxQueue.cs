using Hiram.Application.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Hiram.Infrastructure.Persistence;

public sealed class OutboxQueue : IOutboxQueue
{
    private const int MaximumBatchSize = 100;
    private const int MaximumErrorLength = 4000;
    private const string ClaimSql = """
        WITH candidates AS (
            SELECT id
            FROM notifications.outbox_messages
            WHERE processed_at_utc IS NULL
              AND available_at <= @now
              AND (lease_until IS NULL OR lease_until <= @now)
            ORDER BY available_at, created_at_utc
            FOR UPDATE SKIP LOCKED
            LIMIT @max_count
        )
        UPDATE notifications.outbox_messages AS message
        SET lease_until = @lease_until,
            attempt_count = message.attempt_count + 1
        FROM candidates
        WHERE message.id = candidates.id
        RETURNING message.id,
                  message.tenant_id,
                  message.type,
                  message.payload::text,
                  message.trace_parent,
                  message.available_at,
                  message.lease_until,
                  message.attempt_count
        """;

    private readonly HiramDbContext _context;

    public OutboxQueue(HiramDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<OutboxLease>> ClaimAsync(
        int maxCount,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (maxCount is < 1 or > MaximumBatchSize)
            throw new ArgumentOutOfRangeException(nameof(maxCount), maxCount, $"Batch size must be between 1 and {MaximumBatchSize}.");
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), leaseDuration, "Lease duration must be positive.");

        var leaseUntil = nowUtc.Add(leaseDuration);
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();

        await using var command = new NpgsqlCommand(ClaimSql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.AddWithValue("now", nowUtc);
        command.Parameters.AddWithValue("lease_until", leaseUntil);
        command.Parameters.AddWithValue("max_count", maxCount);

        var claimed = new List<OutboxLease>(maxCount);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                claimed.Add(new OutboxLease(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetFieldValue<DateTimeOffset>(5),
                    reader.GetFieldValue<DateTimeOffset>(6),
                    reader.GetInt32(7)));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return claimed;
    }

    public async Task<bool> RenewAsync(
        Guid messageId,
        DateTimeOffset expectedLeaseUntil,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), leaseDuration, "Lease duration must be positive.");

        var renewedUntil = nowUtc.Add(leaseDuration);
        var updated = await OwnedLease(messageId, expectedLeaseUntil, nowUtc)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(message => message.LeaseUntil, renewedUntil),
                cancellationToken);
        return updated == 1;
    }

    public async Task<bool> CompleteAsync(
        Guid messageId,
        DateTimeOffset expectedLeaseUntil,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        var updated = await OwnedLease(messageId, expectedLeaseUntil, completedAtUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(message => message.ProcessedAtUtc, completedAtUtc)
                    .SetProperty(message => message.LeaseUntil, (DateTimeOffset?)null)
                    .SetProperty(message => message.LastError, (string?)null),
                cancellationToken);
        return updated == 1;
    }

    public async Task<bool> ScheduleRetryAsync(
        Guid messageId,
        DateTimeOffset expectedLeaseUntil,
        DateTimeOffset nowUtc,
        DateTimeOffset availableAtUtc,
        string error,
        CancellationToken cancellationToken)
    {
        if (availableAtUtc < nowUtc)
            throw new ArgumentOutOfRangeException(nameof(availableAtUtc), availableAtUtc, "Retry cannot be scheduled in the past.");
        if (string.IsNullOrWhiteSpace(error))
            throw new ArgumentException("Retry error is required.", nameof(error));

        var storedError = error.Length <= MaximumErrorLength ? error : error[..MaximumErrorLength];
        var updated = await OwnedLease(messageId, expectedLeaseUntil, nowUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(message => message.AvailableAt, availableAtUtc)
                    .SetProperty(message => message.LeaseUntil, (DateTimeOffset?)null)
                    .SetProperty(message => message.LastError, storedError),
                cancellationToken);
        return updated == 1;
    }

    private IQueryable<Domain.Outbox.OutboxMessage> OwnedLease(
        Guid messageId,
        DateTimeOffset expectedLeaseUntil,
        DateTimeOffset nowUtc) =>
        _context.OutboxMessages.Where(message =>
            message.Id == messageId
            && message.ProcessedAtUtc == null
            && message.LeaseUntil == expectedLeaseUntil
            && message.LeaseUntil > nowUtc);
}
