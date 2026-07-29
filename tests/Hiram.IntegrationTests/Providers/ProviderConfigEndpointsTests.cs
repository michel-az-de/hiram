using System.Net;
using System.Net.Http.Json;
using Hiram.Application.Tenancy;
using Hiram.Contracts;
using Hiram.Domain.Notifications;
using Hiram.Domain.Tenants;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Providers;

[Collection("ApiHost")]
public class ProviderConfigEndpointsTests : IAsyncLifetime
{
    private const string AdminKey = "admin-provider-key";

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

        Environment.SetEnvironmentVariable("ConnectionStrings__Hiram", null);
        Environment.SetEnvironmentVariable("Hiram__AdminKey", null);
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", null);

        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Put_PersistsConfig_WithEncryptedSecret()
    {
        var (client, tenantId) = await NewTenant();

        var response = await client.PutAsJsonAsync("/v1/providers/email",
            new SetProviderConfigRequest("resend", new Dictionary<string, string> { ["from"] = "no-reply@t.dev" }, "re_secret"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var config = await ReadConfig(tenantId);
        Assert.NotNull(config);
        Assert.Equal("resend", config!.Provider);
        Assert.Contains("no-reply@t.dev", config.Settings);

        using var scope = _factory!.Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        Assert.NotNull(config.SecretProtected);
        Assert.NotEqual("re_secret", config.SecretProtected);
        Assert.Equal("re_secret", protector.Unprotect(config.SecretProtected!));
    }

    [Fact]
    public async Task Put_RejectsNonEmailChannel()
    {
        var (client, _) = await NewTenant();

        var response = await client.PutAsJsonAsync("/v1/providers/sms",
            new SetProviderConfigRequest("smtp", new Dictionary<string, string>(), null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_RejectsInternalSmtpHost()
    {
        var (client, _) = await NewTenant();

        var response = await client.PutAsJsonAsync("/v1/providers/email",
            new SetProviderConfigRequest("smtp", new Dictionary<string, string>
            {
                ["host"] = "169.254.169.254", ["port"] = "587", ["from"] = "no-reply@t.dev", ["security"] = "starttls"
            }, "pw"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_RejectsSmtpWithoutTls()
    {
        var (client, _) = await NewTenant();

        var response = await client.PutAsJsonAsync("/v1/providers/email",
            new SetProviderConfigRequest("smtp", new Dictionary<string, string>
            {
                ["host"] = "smtp.example.com", ["port"] = "587", ["from"] = "no-reply@t.dev", ["security"] = "none"
            }, "pw"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProviderConfig_IsScopedToTenant()
    {
        var (clientA, tenantA) = await NewTenant();
        var (_, tenantB) = await NewTenant();

        var response = await clientA.PutAsJsonAsync("/v1/providers/email",
            new SetProviderConfigRequest("resend", new Dictionary<string, string> { ["from"] = "a@t.dev" }, "sa"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.NotNull(await ReadConfig(tenantA));
        Assert.Null(await ReadConfig(tenantB));
    }

    private async Task<TenantProviderConfig?> ReadConfig(Guid tenantId)
    {
        using var scope = _factory!.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITenantProviderConfigStore>();
        return await store.FindAsync(tenantId, NotificationChannel.Email, CancellationToken.None);
    }

    private async Task<(HttpClient Client, Guid TenantId)> NewTenant()
    {
        var admin = _factory!.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Admin-Key", AdminKey);

        var tenantResponse = await admin.PostAsJsonAsync("/v1/admin/tenants", new { name = "easystok", deliveryMode = "live" });
        tenantResponse.EnsureSuccessStatusCode();
        var tenant = (await tenantResponse.Content.ReadFromJsonAsync<TenantCreatedDto>())!;

        var keyResponse = await admin.PostAsJsonAsync("/v1/admin/api-keys", new { tenantId = tenant.Id, name = "server" });
        keyResponse.EnsureSuccessStatusCode();
        var key = (await keyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedDto>())!;

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key.Key);
        return (client, tenant.Id);
    }

    private sealed record TenantCreatedDto(Guid Id, string Name, string DeliveryMode);

    private sealed record ApiKeyCreatedDto(Guid Id, Guid TenantId, string Name, string Key, string Prefix);
}
