using System.Net;
using System.Net.Http.Json;
using Hiram.Contracts;
using Hiram.Domain.Notifications;
using Hiram.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.WhatsApp;

[Collection("ApiHost")]
public class WhatsAppIngestionTests : IAsyncLifetime
{
    private const string AdminKey = "admin-whatsapp-key";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Hiram", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Hiram__AdminKey", AdminKey);
        Environment.SetEnvironmentVariable("Hiram__Workers__Enabled", "false");
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
        Environment.SetEnvironmentVariable("Hiram__Workers__Enabled", null);
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", null);

        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Submit_AcceptsWhatsApp_WithoutASubject()
    {
        var (client, tenantId) = await NewTenant();

        var response = await client.PostAsJsonAsync("/v1/notifications",
            new SubmitNotificationRequest("whatsapp", "+5511982254398", Subject: null, Body: "Seu pedido saiu para entrega."));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = (await response.Content.ReadFromJsonAsync<NotificationAccepted>())!;

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<HiramDbContext>();

        var notification = await context.NotificationRequests.SingleAsync(n => n.Id == accepted.Id);
        Assert.Equal(NotificationChannel.WhatsApp, notification.Channel);
        Assert.Null(notification.Subject);

        // Stored bare: the "whatsapp:" prefix belongs to the adapter, so a replay of this row keeps
        // working even if the address scheme changes on the provider side.
        Assert.Equal("+5511982254398", notification.Recipient);

        // The outbox row is what the worker routes on, so the channel has to reach it under its own type.
        var outbox = await context.OutboxMessages.Where(o => o.TenantId == tenantId).ToListAsync();
        Assert.Equal("whatsapp", Assert.Single(outbox).Type);
    }

    [Fact]
    public async Task Submit_RejectsARecipientThatIsNotE164()
    {
        var (client, _) = await NewTenant();

        var response = await client.PostAsJsonAsync("/v1/notifications",
            new SubmitNotificationRequest("whatsapp", "11982254398", Subject: null, Body: "corpo"));

        // The provider would refuse this anyway, so it never becomes an outbox row that can only fail.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Providers_AcceptTheWhatsAppChannel()
    {
        var (client, _) = await NewTenant();

        var response = await client.PutAsJsonAsync("/v1/providers/whatsapp",
            new SetProviderConfigRequest("twilio-whatsapp", new Dictionary<string, string>
            {
                ["account_sid"] = "AC123", ["from"] = "+14155238886", ["api_key_sid"] = "SK123"
            }, "secret"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Templates_AcceptAWhatsAppChannel_WithoutASubject()
    {
        var (client, _) = await NewTenant();

        var response = await client.PostAsJsonAsync("/v1/templates",
            new CreateTemplateRequest("whatsapp", "entrega", Subject: null, "Ola {{ name }}, seu pedido saiu"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Templates_RejectASubject_OnTheWhatsAppChannel()
    {
        var (client, _) = await NewTenant();

        // WhatsApp has nowhere to render a subject, so storing one would keep a value nobody ever reads.
        var response = await client.PostAsJsonAsync("/v1/templates",
            new CreateTemplateRequest("whatsapp", "com-assunto", "Pedido", "corpo"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminRoutines_AcceptTheWhatsAppChannel()
    {
        var (_, tenantId) = await NewTenant();
        var admin = _factory!.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Admin-Key", AdminKey);

        // A routine that names whatsapp is what the event fan-out reads, so the admin surface has to store it.
        var response = await admin.PostAsJsonAsync("/v1/admin/routines", new
        {
            tenantId,
            eventType = "pedido_enviado",
            templateName = "entrega",
            channels = new[] { "whatsapp" },
            category = "transactional",
            active = true
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Consent_AcceptsTheWhatsAppChannel()
    {
        var (client, _) = await NewTenant();

        // Without this surface the channel could never send at all: consent is fail-closed on WhatsApp in
        // every category, so an absent record denies even a transactional message.
        var response = await client.PostAsJsonAsync("/v1/consent",
            new SetConsentRequest(Guid.NewGuid(), "whatsapp", "transactional", OptIn: true));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<(HttpClient Client, Guid TenantId)> NewTenant()
    {
        var admin = _factory!.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Admin-Key", AdminKey);

        var tenantResponse = await admin.PostAsJsonAsync("/v1/admin/tenants", new { name = "whatsapp-tenant", deliveryMode = "live" });
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
