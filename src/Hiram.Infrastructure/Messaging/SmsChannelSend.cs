using Hiram.Application.Delivery;

namespace Hiram.Infrastructure.Messaging;

public sealed class SmsChannelSend : ChannelSend
{
    private readonly ISmsProvider _provider;
    private readonly SmsMessage _message;
    private readonly SmsProviderSettings _settings;

    public SmsChannelSend(ISmsProvider provider, SmsMessage message, SmsProviderSettings settings)
        : base(provider.Name, $"{message.Recipient}\n{message.Body}")
    {
        _provider = provider;
        _message = message;
        _settings = settings;
    }

    public override Task<SendOutcome> SendAsync(CancellationToken cancellationToken) =>
        _provider.SendAsync(_message, _settings, cancellationToken);
}
