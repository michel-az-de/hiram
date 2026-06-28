using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Hiram.Api.Health;

public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await redis.GetDatabase().PingAsync();
        return HealthCheckResult.Healthy();
    }
}
