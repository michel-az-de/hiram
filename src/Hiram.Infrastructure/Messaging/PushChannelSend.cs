using Hiram.Application.Delivery;
using Hiram.Application.Push;
using Hiram.Domain.Push;

namespace Hiram.Infrastructure.Messaging;

public sealed class PushChannelSend : ChannelSend
{
    private readonly IPushSender _sender;
    private readonly PushSubscription _subscription;
    private readonly string _payload;

    public PushChannelSend(
        string provider,
        IPushSender sender,
        PushSubscription subscription,
        string payload,
        string canonicalPayload)
        : base(provider, canonicalPayload)
    {
        _sender = sender;
        _subscription = subscription;
        _payload = payload;
    }

    public override Task<SendOutcome> SendAsync(CancellationToken cancellationToken) =>
        _sender.SendAsync(_subscription, _payload, cancellationToken);
}
