namespace Hiram.Domain.Webhooks;

public sealed class WebhookEndpoint
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Url { get; private set; }

    // The HMAC secret, encrypted at rest. The dispatcher decrypts it to sign the callback body.
    public string SecretProtected { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    // Parameterless ctor exists so EF Core can materialize rows without re-running creation invariants.
    private WebhookEndpoint()
    {
        Url = null!;
        SecretProtected = null!;
    }

    public WebhookEndpoint(Guid id, Guid tenantId, string url, string secretProtected, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Webhook endpoint id is required.", nameof(id));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Url is required.", nameof(url));
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Url must be an absolute http or https URI.", nameof(url));
        if (string.IsNullOrWhiteSpace(secretProtected))
            throw new ArgumentException("Secret is required.", nameof(secretProtected));

        Id = id;
        TenantId = tenantId;
        Url = url;
        SecretProtected = secretProtected;
        CreatedAtUtc = createdAtUtc;
    }
}
