namespace Hiram.Api;

// F0 has no tenant management yet, so every request is attributed to a single well-known
// development tenant. Real multi-tenant resolution arrives with API keys in F1.
internal static class DevTenant
{
    public static readonly Guid Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
}
