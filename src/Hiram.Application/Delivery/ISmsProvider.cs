namespace Hiram.Application.Delivery;

public interface ISmsProvider
{
    // Stable identifier matching the provider column in tenant_provider_configs, e.g. "twilio-sms".
    string Name { get; }

    Task<SendOutcome> SendAsync(SmsMessage message, SmsProviderSettings settings, CancellationToken cancellationToken);
}
