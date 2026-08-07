using Hiram.Application.Delivery;

namespace Hiram.Infrastructure.Messaging;

public sealed class EmailChannelSend : ChannelSend
{
    private readonly IEmailProvider _provider;
    private readonly EmailMessage _message;
    private readonly EmailProviderSettings _settings;

    public EmailChannelSend(ResolvedEmailProvider resolved, EmailMessage message)
        : base(resolved.Provider.Name, Canonical(message))
    {
        _provider = resolved.Provider;
        _settings = resolved.Settings;
        _message = message;
    }

    public override Task<SendOutcome> SendAsync(CancellationToken cancellationToken) =>
        _provider.SendAsync(_message, _settings, cancellationToken);

    private static string Canonical(EmailMessage message) =>
        $"{message.Recipient}\n{message.Subject}\n{message.Body}";
}
