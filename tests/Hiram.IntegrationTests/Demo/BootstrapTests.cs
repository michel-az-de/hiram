using System.Net;
using System.Net.Http.Json;
using Hiram.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Hiram.IntegrationTests.Demo;

[Collection("ApiHost")]
public class BootstrapTests : IAsyncLifetime
{
    private const string AdminKey = "admin-test-key";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        Environment.SetEnvironmentVariable("ConnectionStrings__Hiram", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", _redis.GetConnectionString());
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
    public async Task Bootstrap_CreatesShadowDemoTenant_AndReturnsFreshApiKey()
    {
        var result = await Bootstrap(AdminClient());

        Assert.NotEqual(Guid.Empty, result.TenantId);
        Assert.Equal("hiram-demo", result.TenantName);
        Assert.Equal("shadow", result.DeliveryMode);
        Assert.StartsWith("hk_live_", result.ApiKey);
        Assert.Equal("hk_live_", result.ApiKeyPrefix);
    }

    [Fact]
    public async Task Bootstrap_IsIdempotent_ReturningSameTenant_WithRotatedKey()
    {
        var admin = AdminClient();

        var first = await Bootstrap(admin);
        var second = await Bootstrap(admin);

        Assert.Equal(first.TenantId, second.TenantId);
        Assert.NotEqual(first.ApiKeyId, second.ApiKeyId);
        Assert.NotEqual(first.ApiKey, second.ApiKey);
    }

    [Fact]
    public async Task Bootstrap_WithoutAdminKey_IsRejected()
    {
        var response = await _factory!.CreateClient().PostAsync("/demo/bootstrap", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_IsServedInDemoEnvironment()
    {
        await using var demo = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Demo"));
        var client = demo.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", AdminKey);

        var response = await client.PostAsync("/demo/bootstrap", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_InDemoEnvironment_StillRequiresAdminKey()
    {
        await using var demo = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Demo"));

        var response = await demo.CreateClient().PostAsync("/demo/bootstrap", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_IsNotServedInProduction()
    {
        await using var production = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var client = production.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", AdminKey);

        var response = await client.PostAsync("/demo/bootstrap", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_ReturnsKeyThatAuthenticatesNotifications()
    {
        var result = await Bootstrap(AdminClient());

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", result.ApiKey);

        var response = await client.PostAsJsonAsync(
            "/v1/notifications",
            new SubmitNotificationRequest("email", "ops@example.com", "hello", "bootstrap-1"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private HttpClient AdminClient()
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", AdminKey);
        return client;
    }

    private static async Task<DemoBootstrapDto> Bootstrap(HttpClient admin)
    {
        var response = await admin.PostAsync("/demo/bootstrap", content: null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DemoBootstrapDto>())!;
    }

    private sealed record DemoBootstrapDto(
        Guid TenantId,
        string TenantName,
        string DeliveryMode,
        Guid ApiKeyId,
        string ApiKeyName,
        string ApiKey,
        string ApiKeyPrefix);
}
