using System.Diagnostics;
using Hiram.Application.Abstractions;
using Hiram.Application.Notifications;
using Hiram.Domain.Notifications;
using Hiram.Domain.Outbox;
using Hiram.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace Hiram.Infrastructure.Persistence;

public sealed class DeadLetterReplay : IDeadLetterReplay
{
    private readonly HiramDbContext _context;
    private readonly IClock _clock;

    public DeadLetterReplay(HiramDbContext context, IClock clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<ReplayOutcome> ReplayAsync(Guid tenantId, Guid notificationId, CancellationToken cancellationToken)
    {
        var belongsToTenant = await _context.NotificationRequests
            .AnyAsync(n => n.Id == notificationId && n.TenantId == tenantId, cancellationToken);
        if (!belongsToTenant)
            return ReplayOutcome.NotFound;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // Guarded transition: under Read Committed only one concurrent replay moves the row out of DeadLettered,
        // the loser updates zero rows, so the outbox row is written exactly once no matter how many callers race.
        var moved = await _context.NotificationRequests
            .Where(n => n.Id == notificationId && n.TenantId == tenantId && n.Status == NotificationStatus.DeadLettered)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.Status, NotificationStatus.Queued), cancellationToken);

        if (moved == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            var current = await _context.NotificationRequests
                .Where(n => n.Id == notificationId && n.TenantId == tenantId)
                .Select(n => (NotificationStatus?)n.Status)
                .FirstOrDefaultAsync(cancellationToken);
            return current == NotificationStatus.Queued ? ReplayOutcome.AlreadyReplayed : ReplayOutcome.NotDeadLettered;
        }

        var deadLetter = await _context.DeadLetterMessages
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.NotificationId == notificationId && d.ReplayedAtUtc == null, cancellationToken);
        if (deadLetter is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ReplayOutcome.NotDeadLettered;
        }

        // Reuse the stored payload verbatim and a fresh trace, so the worker redelivers exactly what failed.
        _context.OutboxMessages.Add(new OutboxMessage(
            Guid.NewGuid(), tenantId, RoutingKeyFor(deadLetter.Channel), deadLetter.Payload, _clock.UtcNow, Activity.Current?.Id));
        deadLetter.MarkReplayed(_clock.UtcNow);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        HiramDiagnostics.NotificationsReplayed.Add(1);
        return ReplayOutcome.Replayed;
    }

    private static string RoutingKeyFor(NotificationChannel channel) => channel switch
    {
        NotificationChannel.Email => "email",
        NotificationChannel.Push => "push",
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "No routing key for channel.")
    };
}
