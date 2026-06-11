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

    public Task<NotificationRequest?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        _context.NotificationRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
}
