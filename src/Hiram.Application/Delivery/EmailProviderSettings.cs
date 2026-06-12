namespace Hiram.Application.Delivery;

// Resolved, ready to use configuration for a single send: the non-secret values parsed from the tenant
// config (or platform defaults) plus the decrypted secret. Provider adapters read what they need.
public sealed record EmailProviderSettings(IReadOnlyDictionary<string, string> Values, string? Secret);
