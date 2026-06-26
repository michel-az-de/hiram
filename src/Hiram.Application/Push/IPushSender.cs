using Hiram.Application.Delivery;
using Hiram.Domain.Push;

namespace Hiram.Application.Push;

public interface IPushSender
{
    Task<SendOutcome> SendAsync(PushSubscription subscription, string payload, CancellationToken cancellationToken);
}
