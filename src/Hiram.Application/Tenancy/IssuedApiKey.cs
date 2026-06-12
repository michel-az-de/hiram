using Hiram.Domain.Tenants;

namespace Hiram.Application.Tenancy;

// Pairs the persisted key with its clear secret. The clear value exists only in the issuing response
// and is never stored, so it lives here just long enough to reach the caller.
public sealed record IssuedApiKey(ApiKey ApiKey, string ClearKey);
