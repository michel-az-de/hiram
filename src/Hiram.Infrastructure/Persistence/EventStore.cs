using Hiram.Application.Events;
using Hiram.Domain.Events;
using Hiram.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hiram.Infrastructure.Persistence;

public sealed class EventStore : IEventStore
{
    private const string EventIdIndex = "ux_events_tenant_event_id";

    private readonly HiramDbContext _context;

    public EventStore(HiramDbContext context)
    {
        _context = context;
    }

    public async Task SaveAsync(NotificationEvent @event, OutboxMessage outbox, CancellationToken cancellationToken)
    {
        // One transaction so the event and its outbox row commit together or not at all.
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        _context.Events.Add(@event);
        _context.OutboxMessages.Add(outbox);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsEventConflict(ex))
        {
            throw new DuplicateEventException();
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public Task<Guid?> FindIdByEventIdAsync(Guid tenantId, string eventId, CancellationToken cancellationToken) =>
        _context.Events
            .Where(e => e.TenantId == tenantId && e.EventId == eventId)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static bool IsEventConflict(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: EventIdIndex
        };
}
