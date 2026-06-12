using Hiram.Application.Notifications;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Hiram.Infrastructure.Caching;

public sealed class RedisIdempotencyKeys : IIdempotencyKeys
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisIdempotencyKeys> _logger;

    public RedisIdempotencyKeys(IConnectionMultiplexer redis, ILogger<RedisIdempotencyKeys> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<Guid?> TryClaimAsync(Guid tenantId, string key, Guid notificationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var redisKey = KeyFor(tenantId, key);
        try
        {
            var database = _redis.GetDatabase();
            var claimed = await database.StringSetAsync(redisKey, notificationId.ToString(), Window, When.NotExists);
            if (claimed)
                return null;

            var existing = await database.StringGetAsync(redisKey);
            return Guid.TryParse((string?)existing, out var existingId) ? existingId : null;
        }
        catch (RedisException ex)
        {
            // The durable guarantee is the Postgres unique index. With Redis unavailable we defer to the
            // database to arbitrate and only lose the fast path.
            _logger.LogWarning(ex, "Redis unavailable while claiming idempotency key; deferring to the database");
            return null;
        }
    }

    public async Task StoreAsync(Guid tenantId, string key, Guid notificationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await _redis.GetDatabase().StringSetAsync(KeyFor(tenantId, key), notificationId.ToString(), Window);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis unavailable while caching idempotency result");
        }
    }

    public async Task ReleaseAsync(Guid tenantId, string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await _redis.GetDatabase().KeyDeleteAsync(KeyFor(tenantId, key));
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis unavailable while releasing idempotency claim");
        }
    }

    private static RedisKey KeyFor(Guid tenantId, string key) => $"idem:{tenantId:N}:{key}";
}
