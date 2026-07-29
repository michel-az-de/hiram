using System.Text;
using Hiram.Application.Outbox;

namespace Hiram.Infrastructure.Messaging;

public sealed class OutboxMessageDispatcher
{
    private readonly EmailNotificationProcessor _email;
    private readonly EventMessageProcessor _event;
    private readonly PushNotificationProcessor _push;
    private readonly WebhookDeliveryProcessor _webhook;

    public OutboxMessageDispatcher(
        EmailNotificationProcessor email,
        EventMessageProcessor @event,
        PushNotificationProcessor push,
        WebhookDeliveryProcessor webhook)
    {
        _email = email;
        _event = @event;
        _push = push;
        _webhook = webhook;
    }

    public Task DispatchAsync(OutboxLease message, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(message.Payload).AsMemory();
        return message.Type switch
        {
            HiramTopology.EmailRoutingKey => _email.ProcessAsync(body, cancellationToken),
            HiramTopology.EventRoutingKey => _event.ProcessAsync(body, cancellationToken),
            HiramTopology.PushRoutingKey => _push.ProcessAsync(body, cancellationToken),
            HiramTopology.WebhookRoutingKey => _webhook.ProcessAsync(body, cancellationToken),
            _ => throw new PoisonMessageException($"Outbox message type '{message.Type}' is not supported.")
        };
    }
}
