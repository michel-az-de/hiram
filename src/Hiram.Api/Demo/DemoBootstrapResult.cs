namespace Hiram.Api.Demo;

internal sealed record DemoBootstrapResult(
    Guid TenantId,
    string TenantName,
    string DeliveryMode,
    Guid ApiKeyId,
    string ApiKeyName,
    string ApiKey,
    string ApiKeyPrefix);
