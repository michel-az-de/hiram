namespace Hiram.Application.Outbox;

public interface IOutboxQueue
{
    Task<IReadOnlyList<OutboxLease>> ClaimAsync(
        int maxCount,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> RenewAsync(
        Guid messageId,
        DateTimeOffset expectedLeaseUntil,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> CompleteAsync(
        Guid messageId,
        DateTimeOffset expectedLeaseUntil,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken);

    Task<bool> ScheduleRetryAsync(
        Guid messageId,
        DateTimeOffset expectedLeaseUntil,
        DateTimeOffset nowUtc,
        DateTimeOffset availableAtUtc,
        string error,
        CancellationToken cancellationToken);
}
