using Hiram.Application.Tenancy;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Hiram.Infrastructure.Caching;

public sealed class RedisApiKeyUsageThrottle : IApiKeyUsageThrottle
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisApiKeyUsageThrottle> _logger;

    public RedisApiKeyUsageThrottle(IConnectionMultiplexer redis, ILogger<RedisApiKeyUsageThrottle> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<bool> ShouldRecordUsageAsync(Guid apiKeyId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var marker = $"apikey:lastused:{apiKeyId:N}";
        try
        {
            // SET NX with a TTL: the first caller in the window wins and records usage, the rest are throttled.
            return await _redis.GetDatabase().StringSetAsync(marker, "1", Window, When.NotExists);
        }
        catch (RedisException ex)
        {
            // The throttle is a write optimization, never an auth gate. A Redis outage must not fail the
            // request, so we skip the last_used_at update for this call.
            _logger.LogWarning(ex, "Redis unavailable while throttling api key usage; skipping last_used update");
            return false;
        }
    }
}
