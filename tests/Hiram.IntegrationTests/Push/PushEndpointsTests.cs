using System.Net;
using System.Net.Http.Json;
using Hiram.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Push;

[Collection("ApiHost")]
public class PushEndpointsTests : IAsyncLifetime
{
    private const string AdminKey = "admin-push-key";
    private const string VapidPublicKey = "test-vapid-public-key";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Hiram", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Hiram__AdminKey", AdminKey);
        Environment.SetEnvironmentVariable("Hiram__Push__Vapid__PublicKey", VapidPublicKey);
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", "true");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();

        foreach (var name in new[]
        {
            "ConnectionStrings__Hiram", "Hiram__AdminKey",
            "Hiram__Push__Vapid__PublicKey", "OTEL_SDK_DISABLED"
        })
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Register_Then_List_RoundTrips()
    {
        var client = await NewTenantClient();

        var created = await client.PostAsJsonAsync("/v1/push-subscriptions",
            new RegisterPushSubscriptionRequest("https://push.example.com/a", "p256dh-key", "auth-secret"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var list = await client.GetFromJsonAsync<List<PushSubscriptionResponse>>("/v1/push-subscriptions");

        Assert.Single(list!);
        Assert.Equal("https://push.example.com/a", list![0].Endpoint);
    }

    [Fact]
    public async Task Register_RejectsInvalidEndpoint()
    {
        var client = await NewTenantClient();

        var response = await client.PostAsJsonAsync("/v1/push-subscriptions",
            new RegisterPushSubscriptionRequest("not-a-uri", "p", "a"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_RejectsDuplicateEndpoint()
    {
        var client = await NewTenantClient();
        var request = new RegisterPushSubscriptionRequest("https://push.example.com/dup", "p256dh", "auth");

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/v1/push-subscriptions", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/v1/push-subscriptions", request)).StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesSubscription()
    {
        var client = await NewTenantClient();
        var created = await client.PostAsJsonAsync("/v1/push-subscriptions",
            new RegisterPushSubscriptionRequest("https://push.example.com/x", "p", "a"));
        var id = (await created.Content.ReadFromJsonAsync<PushSubscriptionResponse>())!.Id;

        var deleted = await client.DeleteAsync($"/v1/push-subscriptions/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var list = await client.GetFromJsonAsync<List<PushSubscriptionResponse>>("/v1/push-subscriptions");
        Assert.Empty(list!);
    }

    [Fact]
    public async Task VapidPublicKey_IsExposed()
    {
        var client = await NewTenantClient();

        var key = await client.GetFromJsonAsync<VapidPublicKeyResponse>("/v1/push/vapid-public-key");

        Assert.Equal(VapidPublicKey, key!.PublicKey);
    }

    private async Task<HttpClient> NewTenantClient()
    {
        var admin = _factory!.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Admin-Key", AdminKey);

        var tenantResponse = await admin.PostAsJsonAsync("/v1/admin/tenants", new { name = "casa", deliveryMode = "live" });
        tenantResponse.EnsureSuccessStatusCode();
        var tenant = await tenantResponse.Content.ReadFromJsonAsync<TenantCreatedDto>();

        var keyResponse = await admin.PostAsJsonAsync("/v1/admin/api-keys", new { tenantId = tenant!.Id, name = "server" });
        keyResponse.EnsureSuccessStatusCode();
        var key = await keyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedDto>();

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key!.Key);
        return client;
    }

    private sealed record TenantCreatedDto(Guid Id, string Name, string DeliveryMode);

    private sealed record ApiKeyCreatedDto(Guid Id, Guid TenantId, string Name, string Key, string Prefix);
}
