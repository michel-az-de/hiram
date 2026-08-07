using Hiram.Domain.Notifications;

namespace Hiram.Infrastructure.Messaging;

// Everything that genuinely differs between channels: which provider answers for this tenant and what
// exactly gets sent. Claim, shadow, retry, attempts, dead letter and status webhook are the same for
// every channel and live in ChannelDeliveryProcessor.
public interface IChannelDelivery
{
    Task<ChannelSend> ResolveAsync(NotificationRequest notification, CancellationToken cancellationToken);
}
