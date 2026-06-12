using Hiram.Domain.Tenants;

namespace Hiram.Application.Tenancy;

public interface IApiKeyStore
{
    Task AddAsync(ApiKey apiKey, CancellationToken cancellationToken);

    Task<ApiKey?> FindByHashAsync(string keyHash, CancellationToken cancellationToken);

    Task<bool> RevokeAsync(Guid apiKeyId, DateTimeOffset whenUtc, CancellationToken cancellationToken);

    Task RecordUsageAsync(Guid apiKeyId, DateTimeOffset whenUtc, CancellationToken cancellationToken);
}
