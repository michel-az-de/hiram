using System.Net;
using System.Net.Http.Json;
using Hiram.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Hiram.IntegrationTests.Idempotency;

[Collection("ApiHost")]
public class IdempotencyTests : IAsyncLifetime
{
    private const string AdminKey = "admin-idempotency-key";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();
    private WebApplicationFactory<Program>? _factory;
    private string _redisConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());
        _redisConnectionString = _redis.GetConnectionString();

        Environment.SetEnvironmentVariable("ConnectionStrings__Hiram", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", _redisConnectionString);
        Environment.SetEnvironmentVariable("Hiram__AdminKey", AdminKey);
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", "true");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Hiram", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", null);
        Environment.SetEnvironmentVariable("Hiram__AdminKey", null);
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", null);

        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task RepeatedKey_ReturnsSameId_AndMarksReplay()
    {
        var client = await ClientForNewTenant();

        var first = await Post(client, "evt-1");
        var second = await Post(client, "evt-1");

        Assert.Equal(first.Id, second.Id);
        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
    }

    [Fact]
    public async Task RedisCleared_StillReplaysViaDatabaseBackstop()
    {
        var client = await ClientForNewTenant();

        var first = await Post(client, "evt-2");
        await FlushRedis();
        var second = await Post(client, "evt-2");

        Assert.Equal(first.Id, second.Id);
        Assert.True(second.Replayed);
    }

    [Fact]
    public async Task SameKeyAcrossTenants_DoesNotCollide()
    {
        var tenantA = await ClientForNewTenant();
        var tenantB = await ClientForNewTenant();

        var a = await Post(tenantA, "evt-3");
        var b = await Post(tenantB, "evt-3");

        Assert.NotEqual(a.Id, b.Id);
        Assert.False(a.Replayed);
        Assert.False(b.Replayed);
    }

    private async Task<(Guid Id, bool Replayed)> Post(HttpClient client, string idempotencyKey)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/notifications")
        {
            Content = JsonContent.Create(new SubmitNotificationRequest("email", "ops@example.com", "hello", "f1"))
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);

        var response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AcceptedDto>();
        var replayed = response.Headers.TryGetValues("Idempotency-Replayed", out var values)
            && values.Contains("true");

        return (body!.Id, replayed);
    }

    private async Task<HttpClient> ClientForNewTenant()
    {
        var admin = _factory!.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Admin-Key", AdminKey);

        var tenantResponse = await admin.PostAsJsonAsync("/v1/admin/tenants", new { name = "easystok", deliveryMode = "live" });
        tenantResponse.EnsureSuccessStatusCode();
        var tenant = await tenantResponse.Content.ReadFromJsonAsync<TenantCreatedDto>();

        var keyResponse = await admin.PostAsJsonAsync("/v1/admin/api-keys", new { tenantId = tenant!.Id, name = "server" });
        keyResponse.EnsureSuccessStatusCode();
        var key = await keyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedDto>();

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key!.Key);
        return client;
    }

    private async Task FlushRedis()
    {
        var options = ConfigurationOptions.Parse(_redisConnectionString);
        options.AllowAdmin = true;
        await using var admin = await ConnectionMultiplexer.ConnectAsync(options);
        await admin.GetServer(admin.GetEndPoints()[0]).FlushDatabaseAsync();
    }

    private sealed record AcceptedDto(Guid Id, string Status);

    private sealed record TenantCreatedDto(Guid Id, string Name, string DeliveryMode);

    private sealed record ApiKeyCreatedDto(Guid Id, Guid TenantId, string Name, string Key, string Prefix);
}
