namespace Hiram.Domain.Tenants;

public sealed class Tenant
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public DeliveryMode DeliveryMode { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    // Parameterless ctor exists so EF Core can materialize rows without re-running creation invariants.
    private Tenant()
    {
        Name = null!;
    }

    public Tenant(Guid id, string name, DeliveryMode deliveryMode, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tenant name is required.", nameof(name));
        if (!Enum.IsDefined(deliveryMode))
            throw new ArgumentOutOfRangeException(nameof(deliveryMode), deliveryMode, "Unknown delivery mode.");

        Id = id;
        Name = name;
        DeliveryMode = deliveryMode;
        CreatedAtUtc = createdAtUtc;
    }
}
