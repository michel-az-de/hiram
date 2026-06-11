using Hiram.Application.Notifications;
using Hiram.Domain.Notifications;
using Hiram.Domain.Outbox;

namespace Hiram.Infrastructure.Persistence;

public sealed class NotificationStore : INotificationStore
{
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
        await _context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
