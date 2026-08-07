using Hiram.Application.Delivery;
using Hiram.Domain.Notifications;

namespace Hiram.Infrastructure.Messaging;

public sealed class EmailChannelDelivery : IChannelDelivery
{
    private readonly EmailProviderResolver _resolver;

    public EmailChannelDelivery(EmailProviderResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task<ChannelSend> ResolveAsync(NotificationRequest notification, CancellationToken cancellationToken)
    {
        var resolved = await _resolver.ResolveAsync(notification.TenantId, cancellationToken);
        var message = new EmailMessage(notification.Recipient, notification.Subject, notification.Body);
        return new EmailChannelSend(resolved, message);
    }
}
