using Hiram.Infrastructure.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Hiram.IntegrationTests.Tenancy;

public class ApiKeyUsageThrottleTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();
    private IConnectionMultiplexer? _multiplexer;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        _multiplexer = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_multiplexer is not null)
            await _multiplexer.DisposeAsync();
        await _redis.DisposeAsync();
    }

    private RedisApiKeyUsageThrottle NewThrottle() =>
        new(_multiplexer!, NullLogger<RedisApiKeyUsageThrottle>.Instance);

    [Fact]
    public async Task ShouldRecordUsage_IsTrueOnce_ThenThrottledWithinWindow()
    {
        var throttle = NewThrottle();
        var apiKeyId = Guid.NewGuid();

        Assert.True(await throttle.ShouldRecordUsageAsync(apiKeyId, CancellationToken.None));
        Assert.False(await throttle.ShouldRecordUsageAsync(apiKeyId, CancellationToken.None));
    }

    [Fact]
    public async Task ShouldRecordUsage_ThrottlesEachKeyIndependently()
    {
        var throttle = NewThrottle();

        Assert.True(await throttle.ShouldRecordUsageAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.True(await throttle.ShouldRecordUsageAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
