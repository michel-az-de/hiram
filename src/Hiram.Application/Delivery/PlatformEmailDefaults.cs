namespace Hiram.Application.Delivery;

// Platform wide fallback used when a tenant has no provider config of its own. Populated from environment
// configuration in the composition root.
public sealed record PlatformEmailDefaults(string Provider, string? Secret, IReadOnlyDictionary<string, string> Settings);
