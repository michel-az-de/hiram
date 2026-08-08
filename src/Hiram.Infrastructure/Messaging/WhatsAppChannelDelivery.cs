using System.Text.Json;
using Hiram.Application.Delivery;
using Hiram.Application.Tenancy;
using Hiram.Domain.Notifications;

namespace Hiram.Infrastructure.Messaging;

public sealed class WhatsAppChannelDelivery : IChannelDelivery
{
    private readonly ITenantProviderConfigStore _configs;
    private readonly ISecretProtector _protector;
    private readonly IReadOnlyDictionary<string, IWhatsAppProvider> _providers;

    public WhatsAppChannelDelivery(
        ITenantProviderConfigStore configs,
        ISecretProtector protector,
        IEnumerable<IWhatsAppProvider> providers)
    {
        _configs = configs;
        _protector = protector;
        _providers = providers.ToDictionary(provider => provider.Name, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ChannelSend> ResolveAsync(NotificationRequest notification, CancellationToken cancellationToken)
    {
        var canonical = $"{notification.Recipient}\n{notification.Body}";

        // Unlike email there is no platform fallback: sending WhatsApp spends the tenant's own provider
        // credit and goes out from its own sender, so an unconfigured tenant is a configuration error to
        // surface, not a default to guess.
        var config = await _configs.FindAsync(notification.TenantId, NotificationChannel.WhatsApp, cancellationToken);
        if (config is null)
            return new UnresolvedSend("whatsapp", "provider_not_configured", canonical);

        if (!_providers.TryGetValue(config.Provider, out var provider))
            return new UnresolvedSend(config.Provider, "provider_not_registered", canonical);

        var secret = config.SecretProtected is null ? null : _protector.Unprotect(config.SecretProtected);
        var settings = new WhatsAppProviderSettings(ParseSettings(config.Settings), secret, ProviderConfigOrigin.Tenant);

        return new WhatsAppChannelSend(provider, new WhatsAppMessage(notification.Recipient, notification.Body), settings);
    }

    private static IReadOnlyDictionary<string, string> ParseSettings(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
}
