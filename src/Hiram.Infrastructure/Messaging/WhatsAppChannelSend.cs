using Hiram.Application.Delivery;

namespace Hiram.Infrastructure.Messaging;

public sealed class WhatsAppChannelSend : ChannelSend
{
    private readonly IWhatsAppProvider _provider;
    private readonly WhatsAppMessage _message;
    private readonly WhatsAppProviderSettings _settings;

    public WhatsAppChannelSend(IWhatsAppProvider provider, WhatsAppMessage message, WhatsAppProviderSettings settings)
        : base(provider.Name, message.Canonical)
    {
        _provider = provider;
        _message = message;
        _settings = settings;
    }

    public override Task<SendOutcome> SendAsync(CancellationToken cancellationToken) =>
        _provider.SendAsync(_message, _settings, cancellationToken);
}
