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

namespace Hiram.IntegrationTests.Sms;

[Collection("ApiHost")]
public class SmsIngestionTests : IAsyncLifetime
{
    private const string AdminKey = "admin-sms-key";

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
    public async Task Submit_AcceptsSms_WithoutASubject()
    {
        var (client, tenantId) = await NewTenant();

        var response = await client.PostAsJsonAsync("/v1/notifications",
            new SubmitNotificationRequest("sms", "+5511982254398", Subject: null, Body: "Seu pedido saiu para entrega."));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = (await response.Content.ReadFromJsonAsync<NotificationAccepted>())!;

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<HiramDbContext>();

        var notification = await context.NotificationRequests.SingleAsync(n => n.Id == accepted.Id);
        Assert.Equal(NotificationChannel.Sms, notification.Channel);
        Assert.Null(notification.Subject);

        // The outbox row is what the worker routes on, so the channel has to reach it under its own type.
        var outbox = await context.OutboxMessages.Where(o => o.TenantId == tenantId).ToListAsync();
        Assert.Equal("sms", Assert.Single(outbox).Type);
    }

    [Fact]
    public async Task Submit_RejectsARecipientThatIsNotE164()
    {
        var (client, _) = await NewTenant();

        var response = await client.PostAsJsonAsync("/v1/notifications",
            new SubmitNotificationRequest("sms", "11982254398", Subject: null, Body: "corpo"));

        // A carrier would refuse this anyway, so it never becomes an outbox row that can only fail.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Submit_StillRequiresASubject_OnEmail()
    {
        var (client, _) = await NewTenant();

        var response = await client.PostAsJsonAsync("/v1/notifications",
            new SubmitNotificationRequest("email", "ops@example.com", Subject: null, Body: "corpo"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Providers_AcceptTheSmsChannel()
    {
        var (client, _) = await NewTenant();

        var response = await client.PutAsJsonAsync("/v1/providers/sms",
            new SetProviderConfigRequest("twilio-sms", new Dictionary<string, string>
            {
                ["account_sid"] = "AC123", ["from"] = "+17372212163", ["api_key_sid"] = "SK123"
            }, "secret"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task AdminRoutines_AcceptTheSmsChannel_AlongsideEmail()
    {
        var (_, tenantId) = await NewTenant();
        var admin = _factory!.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Admin-Key", AdminKey);

        // A routine that names sms is what the event fan-out reads, so the admin surface has to store it.
        var response = await admin.PostAsJsonAsync("/v1/admin/routines", new
        {
            tenantId,
            eventType = "pedido_enviado",
            templateName = "entrega",
            channels = new[] { "email", "sms" },
            category = "transactional",
            active = true
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Consent_AcceptsTheSmsChannel()
    {
        var (client, _) = await NewTenant();

        var response = await client.PostAsJsonAsync("/v1/consent",
            new SetConsentRequest(Guid.NewGuid(), "sms", "marketing", OptIn: true));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<(HttpClient Client, Guid TenantId)> NewTenant()
    {
        var admin = _factory!.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Admin-Key", AdminKey);

        var tenantResponse = await admin.PostAsJsonAsync("/v1/admin/tenants", new { name = "sms-tenant", deliveryMode = "live" });
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

    [Fact]
    public async Task Submit_ReportsHowManySegmentsTheSmsCosts()
    {
        var (client, _) = await NewTenant();

        // 148 characters of a vowel that GSM-7 does not carry: the limit drops from 160 to 70 and the
        // carrier bills three times. Learning that from the response beats learning it from the invoice.
        var expensive = await client.PostAsJsonAsync("/v1/notifications",
            new SubmitNotificationRequest("sms", "+5511982254398", Body: new string('á', 148)));
        var cheap = await client.PostAsJsonAsync("/v1/notifications",
            new SubmitNotificationRequest("sms", "+5511982254398", Body: new string('a', 148)));

        Assert.Equal(3, (await expensive.Content.ReadFromJsonAsync<NotificationAccepted>())!.Segments);
        Assert.Equal(1, (await cheap.Content.ReadFromJsonAsync<NotificationAccepted>())!.Segments);
    }

    [Fact]
    public async Task Submit_ReportsNoSegments_OnAChannelThatIsNotSms()
    {
        var (client, _) = await NewTenant();

        var response = await client.PostAsJsonAsync("/v1/notifications",
            new SubmitNotificationRequest("email", "alguem@example.test", Subject: "Pedido", Body: "Seu pedido saiu."));

        Assert.Null((await response.Content.ReadFromJsonAsync<NotificationAccepted>())!.Segments);
    }
}
