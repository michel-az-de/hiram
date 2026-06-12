using Hiram.Application.Notifications;
using Hiram.Domain.Notifications;
using Hiram.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hiram.Infrastructure.Persistence;

public sealed class NotificationStore : INotificationStore
{
    private const string IdempotencyIndex = "ux_notification_requests_idempotency";

    private readonly HiramDbContext _context;

    public NotificationStore(HiramDbContext context)
    {
        _context = context;
    }

    public async Task SaveAsync(NotificationRequest request, OutboxMessage outbox, CancellationToken cancellationToken)
    {
        // One transaction so the request and its outbox row commit together or not at all.
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        _context.NotificationRequests.Add(request);
        _context.OutboxMessages.Add(outbox);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsIdempotencyConflict(ex))
        {
            throw new DuplicateIdempotencyKeyException();
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static bool IsIdempotencyConflict(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: IdempotencyIndex
        };
}
