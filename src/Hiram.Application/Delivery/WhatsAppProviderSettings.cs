namespace Hiram.Application.Delivery;

// Resolved configuration for a single WhatsApp send: the non-secret values from the tenant config, the
// decrypted secret and where the config came from.
public sealed record WhatsAppProviderSettings(
    IReadOnlyDictionary<string, string> Values, string? Secret, ProviderConfigOrigin Origin);
