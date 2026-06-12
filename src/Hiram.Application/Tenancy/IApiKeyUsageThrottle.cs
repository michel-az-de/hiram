namespace Hiram.Application.Tenancy;

public interface IApiKeyUsageThrottle
{
    // True at most once per throttle window for a given key, so last_used_at is not written on every request.
    Task<bool> ShouldRecordUsageAsync(Guid apiKeyId, CancellationToken cancellationToken);
}
