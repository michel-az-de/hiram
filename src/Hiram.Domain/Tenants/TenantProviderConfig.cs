using Hiram.Domain.Notifications;

namespace Hiram.Domain.Tenants;

public sealed class TenantProviderConfig
{
    public Guid TenantId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public string Provider { get; private set; }

    // Non-secret provider settings as JSON: host, port, from address, sending domain.
    public string Settings { get; private set; }

    // Provider secret (smtp password, api token) protected with Data Protection before it reaches this row.
    public string? SecretProtected { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    // Parameterless ctor exists so EF Core can materialize rows without re-running creation invariants.
    private TenantProviderConfig()
    {
        Provider = null!;
        Settings = null!;
    }

    public TenantProviderConfig(
        Guid tenantId,
        NotificationChannel channel,
        string provider,
        string settings,
        string? secretProtected,
        DateTimeOffset updatedAtUtc)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (!Enum.IsDefined(channel))
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown notification channel.");
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider is required.", nameof(provider));
        if (string.IsNullOrWhiteSpace(settings))
            throw new ArgumentException("Provider settings are required.", nameof(settings));

        TenantId = tenantId;
        Channel = channel;
        Provider = provider;
        Settings = settings;
        SecretProtected = secretProtected;
        UpdatedAtUtc = updatedAtUtc;
    }

    // Replaces the provider choice and its settings while keeping the (tenant, channel) identity, so an
    // upsert reuses the existing row instead of a delete and insert that would churn the primary key.
    public void Reconfigure(string provider, string settings, string? secretProtected, DateTimeOffset updatedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider is required.", nameof(provider));
        if (string.IsNullOrWhiteSpace(settings))
            throw new ArgumentException("Provider settings are required.", nameof(settings));

        Provider = provider;
        Settings = settings;
        SecretProtected = secretProtected;
        UpdatedAtUtc = updatedAtUtc;
    }
}
