namespace Hiram.Application.Delivery;

// Resolved, ready to use configuration for a single send: the non-secret values parsed from the tenant
// config (or platform defaults), the decrypted secret, and where the config came from. Provider adapters
// read what they need; Origin is required so the SSRF and TLS guards on the tenant path cannot be skipped
// by a call site that forgets to set it.
public sealed record EmailProviderSettings(
    IReadOnlyDictionary<string, string> Values, string? Secret, ProviderConfigOrigin Origin);
