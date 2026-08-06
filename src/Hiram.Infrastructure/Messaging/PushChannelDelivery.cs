using System.Text.Json;
using Hiram.Application.Push;
using Hiram.Domain.Notifications;
using Hiram.Domain.Push;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hiram.Infrastructure.Messaging;

public sealed class PushChannelDelivery : IChannelDelivery
{
    public const string ProviderName = "webpush";

    private readonly HiramDbContext _context;
    private readonly IPushSender _sender;

    public PushChannelDelivery(HiramDbContext context, IPushSender sender)
    {
        _context = context;
        _sender = sender;
    }

    public async Task<ChannelSend> ResolveAsync(NotificationRequest notification, CancellationToken cancellationToken)
    {
        var canonical = $"{notification.Recipient}\n{notification.Subject}\n{notification.Body}";

        var subscription = await FindSubscriptionAsync(notification, cancellationToken);
        if (subscription is null)
            return new UnresolvedSend(ProviderName, "subscription_not_found", canonical);

        var payload = JsonSerializer.Serialize(new { title = notification.Subject, body = notification.Body });
        return new PushChannelSend(ProviderName, _sender, subscription, payload, canonical);
    }

    private async Task<PushSubscription?> FindSubscriptionAsync(NotificationRequest notification, CancellationToken cancellationToken)
    {
        // The recipient of a push notification is the subscription id, so anything unparseable is simply
        // a destination that does not exist for this tenant.
        if (!Guid.TryParse(notification.Recipient, out var subscriptionId))
            return null;

        return await _context.PushSubscriptions
            .FirstOrDefaultAsync(s => s.TenantId == notification.TenantId && s.Id == subscriptionId, cancellationToken);
    }
}
