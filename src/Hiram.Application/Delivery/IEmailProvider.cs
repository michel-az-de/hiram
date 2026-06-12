namespace Hiram.Application.Delivery;

public interface IEmailProvider
{
    // Stable identifier matching the provider column in tenant_provider_configs, e.g. "smtp" or "resend".
    string Name { get; }

    Task<SendOutcome> SendAsync(EmailMessage message, EmailProviderSettings settings, CancellationToken cancellationToken);
}
