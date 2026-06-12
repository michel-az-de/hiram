namespace Hiram.Api.Admin;

internal sealed record CreateApiKeyRequest(Guid TenantId, string Name);
