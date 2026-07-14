namespace Hiram.Contracts;

// The tenant's email provider choice. Settings are the non-secret values (host, port, from, security);
// Secret is the plaintext password or token, encrypted before it is stored and never returned.
public sealed record SetProviderConfigRequest(
    string Provider,
    Dictionary<string, string> Settings,
    string? Secret);
