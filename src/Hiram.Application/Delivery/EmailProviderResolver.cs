using System.Text.Json;
using Hiram.Application.Tenancy;
using Hiram.Domain.Notifications;

namespace Hiram.Application.Delivery;

public sealed class EmailProviderResolver
{
    private readonly ITenantProviderConfigStore _configs;
    private readonly ISecretProtector _protector;
    private readonly PlatformEmailDefaults _platform;
    private readonly IReadOnlyDictionary<string, IEmailProvider> _providers;

    public EmailProviderResolver(
        ITenantProviderConfigStore configs,
        ISecretProtector protector,
        PlatformEmailDefaults platform,
        IEnumerable<IEmailProvider> providers)
    {
        _configs = configs;
        _protector = protector;
        _platform = platform;
        _providers = providers.ToDictionary(provider => provider.Name, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ResolvedEmailProvider> ResolveAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var config = await _configs.FindAsync(tenantId, NotificationChannel.Email, cancellationToken);
        if (config is null)
            return new ResolvedEmailProvider(
                ProviderNamed(_platform.Provider),
                new EmailProviderSettings(_platform.Settings, _platform.Secret, ProviderConfigOrigin.Platform));

        var secret = config.SecretProtected is null ? null : _protector.Unprotect(config.SecretProtected);
        return new ResolvedEmailProvider(
            ProviderNamed(config.Provider),
            new EmailProviderSettings(ParseSettings(config.Settings), secret, ProviderConfigOrigin.Tenant));
    }

    private IEmailProvider ProviderNamed(string name)
    {
        if (_providers.TryGetValue(name, out var provider))
            return provider;

        throw new InvalidOperationException($"No email provider named '{name}' is registered.");
    }

    private static IReadOnlyDictionary<string, string> ParseSettings(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
}
