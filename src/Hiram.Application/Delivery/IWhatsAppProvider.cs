namespace Hiram.Application.Delivery;

public interface IWhatsAppProvider
{
    // Stable identifier matching the provider column in tenant_provider_configs, e.g. "twilio-whatsapp".
    string Name { get; }

    Task<SendOutcome> SendAsync(WhatsAppMessage message, WhatsAppProviderSettings settings, CancellationToken cancellationToken);
}
