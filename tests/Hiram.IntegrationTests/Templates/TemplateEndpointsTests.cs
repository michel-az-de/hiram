using System.Net;
using System.Net.Http.Json;
using Hiram.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Templates;

[Collection("ApiHost")]
public class TemplateEndpointsTests : IAsyncLifetime
{
    private const string AdminKey = "admin-template-key";

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
    public async Task Create_Then_Get_RoundTrips()
    {
        var client = await NewTenantClient();

        var created = await client.PostAsJsonAsync("/v1/templates",
            new CreateTemplateRequest("email", "welcome", "Hi {{ name }}", "Welcome {{ name }}"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = (await created.Content.ReadFromJsonAsync<TemplateDto>())!;

        var fetched = await client.GetFromJsonAsync<TemplateDto>($"/v1/templates/{body.Id}");

        Assert.Equal("welcome", fetched!.Name);
        Assert.Equal("Hi {{ name }}", fetched.Subject);
    }

    [Fact]
    public async Task Create_RejectsInvalidTemplateSyntax()
    {
        var client = await NewTenantClient();

        var response = await client.PostAsJsonAsync("/v1/templates",
            new CreateTemplateRequest("email", "broken", "{{ if x }}no end", "body"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_RejectsDuplicateNamePerChannel()
    {
        var client = await NewTenantClient();
        var request = new CreateTemplateRequest("email", "welcome", "Hi {{ name }}", "Welcome {{ name }}");

        var first = await client.PostAsJsonAsync("/v1/templates", request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/v1/templates", request);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Update_ChangesContent()
    {
        var client = await NewTenantClient();
        var created = await client.PostAsJsonAsync("/v1/templates",
            new CreateTemplateRequest("email", "welcome", "Hi {{ name }}", "Welcome {{ name }}"));
        var id = (await created.Content.ReadFromJsonAsync<TemplateDto>())!.Id;

        var updated = await client.PutAsJsonAsync($"/v1/templates/{id}",
            new UpdateTemplateRequest("Hello {{ name }}", "New body {{ name }}"));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = (await updated.Content.ReadFromJsonAsync<TemplateDto>())!;

        Assert.Equal("Hello {{ name }}", body.Subject);
    }

    [Fact]
    public async Task Delete_RemovesTemplate()
    {
        var client = await NewTenantClient();
        var created = await client.PostAsJsonAsync("/v1/templates",
            new CreateTemplateRequest("email", "welcome", "Hi {{ name }}", "Welcome {{ name }}"));
        var id = (await created.Content.ReadFromJsonAsync<TemplateDto>())!.Id;

        var deleted = await client.DeleteAsync($"/v1/templates/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var fetched = await client.GetAsync($"/v1/templates/{id}");
        Assert.Equal(HttpStatusCode.NotFound, fetched.StatusCode);
    }

    [Fact]
    public async Task Templates_AreScopedToTenant()
    {
        var clientA = await NewTenantClient();
        var clientB = await NewTenantClient();

        var created = await clientA.PostAsJsonAsync("/v1/templates",
            new CreateTemplateRequest("email", "welcome", "Hi {{ name }}", "Welcome {{ name }}"));
        var id = (await created.Content.ReadFromJsonAsync<TemplateDto>())!.Id;

        var crossTenant = await clientB.GetAsync($"/v1/templates/{id}");
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);

        var listB = await clientB.GetFromJsonAsync<List<TemplateDto>>("/v1/templates");
        Assert.Empty(listB!);
    }

    [Fact]
    public async Task TemplatedSubmit_MissingTemplate_Returns404()
    {
        var client = await NewTenantClient();

        var response = await client.PostAsJsonAsync("/v1/notifications",
            new { channel = "email", recipient = "ops@example.com", template = "does-not-exist", data = new { name = "Ada" } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TemplatedSubmit_MissingVariable_Returns400()
    {
        var client = await NewTenantClient();
        await client.PostAsJsonAsync("/v1/templates",
            new CreateTemplateRequest("email", "needs-name", "Hi {{ name }}", "Body {{ name }}"));

        var response = await client.PostAsJsonAsync("/v1/notifications",
            new { channel = "email", recipient = "ops@example.com", template = "needs-name", data = new { } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_AcceptsAnSmsTemplate_WithoutASubject()
    {
        var client = await NewTenantClient();

        var created = await client.PostAsJsonAsync("/v1/templates",
            new CreateTemplateRequest("sms", "entrega", Subject: null, "Seu pedido saiu, {{ name }}"));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = (await created.Content.ReadFromJsonAsync<TemplateDto>())!;
        Assert.Equal("sms", body.Channel);
        Assert.Null(body.Subject);
    }

    [Fact]
    public async Task Create_RejectsASubject_OnTheSmsChannel()
    {
        var client = await NewTenantClient();

        // An SMS carries no subject line, so a stored subject would be a value that never reaches anyone.
        var response = await client.PostAsJsonAsync("/v1/templates",
            new CreateTemplateRequest("sms", "entrega", "Nao deveria existir", "corpo"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_StillRequiresASubject_OnTheEmailChannel()
    {
        var client = await NewTenantClient();

        var response = await client.PostAsJsonAsync("/v1/templates",
            new CreateTemplateRequest("email", "welcome", Subject: null, "Welcome"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_AllowsTheSameName_OnEmailAndSms()
    {
        var client = await NewTenantClient();

        var email = await client.PostAsJsonAsync("/v1/templates",
            new CreateTemplateRequest("email", "pedido", "Seu pedido", "Corpo"));
        Assert.Equal(HttpStatusCode.Created, email.StatusCode);

        // The unique index is per tenant and channel, so one routine name can fan out to both channels.
        var sms = await client.PostAsJsonAsync("/v1/templates",
            new CreateTemplateRequest("sms", "pedido", Subject: null, "Corpo"));
        Assert.Equal(HttpStatusCode.Created, sms.StatusCode);
    }

    [Fact]
    public async Task Approve_AcceptsAnSmsTemplate()
    {
        var client = await NewTenantClient();
        var created = await client.PostAsJsonAsync("/v1/templates",
            new CreateTemplateRequest("sms", "entrega", Subject: null, "Seu pedido saiu"));
        var id = (await created.Content.ReadFromJsonAsync<TemplateDto>())!.Id;

        // Without approval the routine engine suppresses the channel, so this is on the fan-out path.
        var approved = await client.PostAsync($"/v1/templates/{id}/approve", null);

        Assert.Equal(HttpStatusCode.NoContent, approved.StatusCode);
    }

    [Fact]
    public async Task Update_AcceptsANullSubject_OnTheSmsChannel()
    {
        var client = await NewTenantClient();
        var created = await client.PostAsJsonAsync("/v1/templates",
            new CreateTemplateRequest("sms", "entrega", Subject: null, "Corpo"));
        var id = (await created.Content.ReadFromJsonAsync<TemplateDto>())!.Id;

        var updated = await client.PutAsJsonAsync($"/v1/templates/{id}",
            new UpdateTemplateRequest(Subject: null, "Outro corpo"));

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = (await updated.Content.ReadFromJsonAsync<TemplateDto>())!;
        Assert.Null(body.Subject);
        Assert.Equal("Outro corpo", body.Body);
    }

    private async Task<HttpClient> NewTenantClient()
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

    private sealed record TenantCreatedDto(Guid Id, string Name, string DeliveryMode);

    private sealed record ApiKeyCreatedDto(Guid Id, Guid TenantId, string Name, string Key, string Prefix);

    private sealed record TemplateDto(Guid Id, string Channel, string Name, string? Subject, string Body);
}
