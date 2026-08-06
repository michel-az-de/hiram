namespace Hiram.Application.Delivery;

// Resolved configuration for a single SMS send: the non-secret values from the tenant config, the
// decrypted secret and where the config came from.
public sealed record SmsProviderSettings(
    IReadOnlyDictionary<string, string> Values, string? Secret, ProviderConfigOrigin Origin);
