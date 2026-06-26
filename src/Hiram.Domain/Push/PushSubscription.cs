namespace Hiram.Domain.Push;

public sealed class PushSubscription
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    // The browser push endpoint and the two keys it handed out: p256dh (ECDH public key) and the auth secret.
    public string Endpoint { get; private set; }
    public string P256dh { get; private set; }
    public string Auth { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    // Parameterless ctor exists so EF Core can materialize rows without re-running creation invariants.
    private PushSubscription()
    {
        Endpoint = null!;
        P256dh = null!;
        Auth = null!;
    }

    public PushSubscription(Guid id, Guid tenantId, string endpoint, string p256dh, string auth, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Subscription id is required.", nameof(id));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint is required.", nameof(endpoint));
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            throw new ArgumentException("Endpoint must be an absolute URI.", nameof(endpoint));
        if (string.IsNullOrWhiteSpace(p256dh))
            throw new ArgumentException("p256dh key is required.", nameof(p256dh));
        if (string.IsNullOrWhiteSpace(auth))
            throw new ArgumentException("auth key is required.", nameof(auth));

        Id = id;
        TenantId = tenantId;
        Endpoint = endpoint;
        P256dh = p256dh;
        Auth = auth;
        CreatedAtUtc = createdAtUtc;
    }
}
