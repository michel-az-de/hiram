using System.Text;
using Hiram.Application.Outbox;

namespace Hiram.Infrastructure.Messaging;

public sealed class OutboxMessageDispatcher
{
    private readonly ChannelDeliveryProcessor _delivery;
    private readonly EmailChannelDelivery _email;
    private readonly PushChannelDelivery _push;
    private readonly EventMessageProcessor _event;
    private readonly WebhookDeliveryProcessor _webhook;

    public OutboxMessageDispatcher(
        ChannelDeliveryProcessor delivery,
        EmailChannelDelivery email,
        PushChannelDelivery push,
        EventMessageProcessor @event,
        WebhookDeliveryProcessor webhook)
    {
        _delivery = delivery;
        _email = email;
        _push = push;
        _event = @event;
        _webhook = webhook;
    }

    public Task DispatchAsync(OutboxLease message, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(message.Payload).AsMemory();
        return message.Type switch
        {
            "email" => _delivery.ProcessAsync(_email, body, cancellationToken),
            "event" => _event.ProcessAsync(body, cancellationToken),
            "push" => _delivery.ProcessAsync(_push, body, cancellationToken),
            "webhook" => _webhook.ProcessAsync(body, cancellationToken),
            _ => throw new PoisonMessageException($"Outbox message type '{message.Type}' is not supported.")
        };
    }
}
