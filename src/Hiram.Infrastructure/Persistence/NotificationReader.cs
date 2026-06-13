using Hiram.Application.Notifications;
using Hiram.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Hiram.Infrastructure.Persistence;

public sealed class NotificationReader : INotificationReader
{
    private readonly HiramDbContext _context;

    public NotificationReader(HiramDbContext context)
    {
        _context = context;
    }

    public Task<NotificationRequest?> FindAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        _context.NotificationRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);

    public Task<Guid?> FindIdByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken cancellationToken) =>
        _context.NotificationRequests
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IdempotencyKey == idempotencyKey)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<NotificationRequest>> QueryAsync(NotificationQuery query, CancellationToken cancellationToken)
    {
        var notifications = _context.NotificationRequests
            .AsNoTracking()
            .Where(x => x.TenantId == query.TenantId);

        if (query.Status is { } status)
            notifications = notifications.Where(x => x.Status == status);
        if (query.Channel is { } channel)
            notifications = notifications.Where(x => x.Channel == channel);
        if (query.Since is { } since)
            notifications = notifications.Where(x => x.CreatedAtUtc >= since);
        if (query.Until is { } until)
            notifications = notifications.Where(x => x.CreatedAtUtc < until);
        if (query.CursorCreatedAtUtc is { } cursorAt && query.CursorId is { } cursorId)
            notifications = notifications.Where(x => x.CreatedAtUtc < cursorAt || (x.CreatedAtUtc == cursorAt && x.Id < cursorId));

        return await notifications
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DeliveryAttempt>> AttemptsAsync(Guid tenantId, Guid notificationId, CancellationToken cancellationToken) =>
        await _context.DeliveryAttempts
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.NotificationId == notificationId)
            .OrderBy(x => x.AttemptNumber)
            .ToListAsync(cancellationToken);
}
