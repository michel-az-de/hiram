using System.Net;
using System.Net.Http.Json;
using Hiram.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Hiram.IntegrationTests.Authentication;

[Collection("ApiHost")]
public class AuthenticationTests : IAsyncLifetime
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
    public async Task IssuedKey_AuthenticatesNotificationRequest()
    {
        var key = await CreateApiKey();

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key.Key);

        var response = await client.PostAsJsonAsync(
            "/v1/notifications",
            new SubmitNotificationRequest("email", "ops@example.com", "hello", "f1"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task MissingApiKey_IsRejected()
    {
        var client = _factory!.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/v1/notifications",
            new SubmitNotificationRequest("email", "ops@example.com", "hello", "f1"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RevokedKey_IsRejected()
    {
        var key = await CreateApiKey();

        var admin = AdminClient();
        var revoke = await admin.DeleteAsync($"/v1/admin/api-keys/{key.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key.Key);

        var response = await client.PostAsJsonAsync(
            "/v1/notifications",
            new SubmitNotificationRequest("email", "ops@example.com", "hello", "f1"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongAdminKey_IsRejected()
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", "not-the-admin-key");

        var response = await client.PostAsJsonAsync("/v1/admin/tenants", new { name = "easystok", deliveryMode = "shadow" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient AdminClient()
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", AdminKey);
        return client;
    }

    private async Task<ApiKeyCreatedDto> CreateApiKey()
    {
        var admin = AdminClient();

        var tenantResponse = await admin.PostAsJsonAsync("/v1/admin/tenants", new { name = "easystok", deliveryMode = "live" });
        tenantResponse.EnsureSuccessStatusCode();
        var tenant = await tenantResponse.Content.ReadFromJsonAsync<TenantCreatedDto>();

        var keyResponse = await admin.PostAsJsonAsync("/v1/admin/api-keys", new { tenantId = tenant!.Id, name = "server" });
        keyResponse.EnsureSuccessStatusCode();
        return (await keyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedDto>())!;
    }

    private sealed record TenantCreatedDto(Guid Id, string Name, string DeliveryMode);

    private sealed record ApiKeyCreatedDto(Guid Id, Guid TenantId, string Name, string Key, string Prefix);
}
