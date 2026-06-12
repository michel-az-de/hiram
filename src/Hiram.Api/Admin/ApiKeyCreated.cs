namespace Hiram.Api.Admin;

// Key is the clear secret, returned only here at creation and never persisted in clear.
internal sealed record ApiKeyCreated(Guid Id, Guid TenantId, string Name, string Key, string Prefix);
