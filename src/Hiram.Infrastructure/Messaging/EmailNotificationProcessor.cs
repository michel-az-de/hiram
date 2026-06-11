using System.Text.Json;
using Hiram.Application.Notifications;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hiram.Infrastructure.Messaging;

public sealed class EmailNotificationProcessor
{
    private readonly HiramDbContext _context;
    private readonly ILogger<EmailNotificationProcessor> _logger;

    public EmailNotificationProcessor(HiramDbContext context, ILogger<EmailNotificationProcessor> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task ProcessAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<OutboxNotificationPayload>(body.Span);
        if (payload is null)
        {
            _logger.LogWarning("Received an email message with an empty payload, skipping");
            return;
        }

        _logger.LogInformation("Would send email for notification {NotificationId}", payload.NotificationId);

        var notification = await _context.NotificationRequests
            .FirstOrDefaultAsync(x => x.Id == payload.NotificationId, cancellationToken);
        if (notification is null)
        {
            _logger.LogWarning("Notification {NotificationId} not found while consuming email message", payload.NotificationId);
            return;
        }

        notification.MarkPublished();
        await _context.SaveChangesAsync(cancellationToken);
    }
}
