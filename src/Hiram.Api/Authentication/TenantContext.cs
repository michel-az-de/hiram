namespace Hiram.Api.Authentication;

// Request scoped holder for the tenant resolved from the API key, read by endpoints to attribute work.
public sealed class TenantContext
{
    public Guid TenantId { get; private set; }
    public bool IsResolved { get; private set; }

    public void Resolve(Guid tenantId)
    {
        TenantId = tenantId;
        IsResolved = true;
    }
}
