namespace Hiram.Domain.Tenants;

public sealed class ApiKey
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; }

    // Only the SHA-256 of the presented key is stored. High entropy keys make a slow hash unnecessary.
    public string KeyHash { get; private set; }

    // First characters of the key, kept in clear to identify a key in listings without revealing it.
    public string KeyPrefix { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public DateTimeOffset? LastUsedAtUtc { get; private set; }

    // Parameterless ctor exists so EF Core can materialize rows without re-running creation invariants.
    private ApiKey()
    {
        Name = null!;
        KeyHash = null!;
        KeyPrefix = null!;
    }

    public ApiKey(Guid id, Guid tenantId, string name, string keyHash, string keyPrefix, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Api key id is required.", nameof(id));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Api key name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(keyHash))
            throw new ArgumentException("Api key hash is required.", nameof(keyHash));
        if (string.IsNullOrWhiteSpace(keyPrefix))
            throw new ArgumentException("Api key prefix is required.", nameof(keyPrefix));

        Id = id;
        TenantId = tenantId;
        Name = name;
        KeyHash = keyHash;
        KeyPrefix = keyPrefix;
        CreatedAtUtc = createdAtUtc;
    }
}
