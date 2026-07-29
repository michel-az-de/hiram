using System.Net;
using System.Net.Http.Json;
using Hiram.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Webhooks;

[Collection("ApiHost")]
public class WebhookEndpointsTests : IAsyncLifetime
{
    private const string AdminKey = "admin-webhook-key";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Hiram", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Hiram__AdminKey", AdminKey);
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", "true");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();

        foreach (var name in new[] { "ConnectionStrings__Hiram", "Hiram__AdminKey", "OTEL_SDK_DISABLED" })
            Environment.SetEnvironmentVariable(name, null);

        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Register_ReturnsSecretOnce_AndListOmitsIt()
    {
        var client = await NewTenantClient();

        var created = await client.PostAsJsonAsync("/v1/webhooks", new RegisterWebhookRequest("https://tenant.example.com/hooks"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var registered = (await created.Content.ReadFromJsonAsync<WebhookRegistered>())!;
        Assert.False(string.IsNullOrEmpty(registered.Secret));

        var list = await client.GetFromJsonAsync<List<WebhookResponse>>("/v1/webhooks");
        Assert.Single(list!);
        Assert.Equal("https://tenant.example.com/hooks", list![0].Url);
    }

    [Fact]
    public async Task Register_RejectsInvalidUrl()
    {
        var client = await NewTenantClient();

        var response = await client.PostAsJsonAsync("/v1/webhooks", new RegisterWebhookRequest("ftp://nope"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_RejectsDuplicateUrl()
    {
        var client = await NewTenantClient();
        var request = new RegisterWebhookRequest("https://tenant.example.com/dup");

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/v1/webhooks", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/v1/webhooks", request)).StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesWebhook()
    {
        var client = await NewTenantClient();
        var created = await client.PostAsJsonAsync("/v1/webhooks", new RegisterWebhookRequest("https://tenant.example.com/x"));
        var id = (await created.Content.ReadFromJsonAsync<WebhookRegistered>())!.Id;

        var deleted = await client.DeleteAsync($"/v1/webhooks/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var list = await client.GetFromJsonAsync<List<WebhookResponse>>("/v1/webhooks");
        Assert.Empty(list!);
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
