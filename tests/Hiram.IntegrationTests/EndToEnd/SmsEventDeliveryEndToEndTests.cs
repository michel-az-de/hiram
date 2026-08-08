using System.Net;
using System.Net.Http.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Hiram.Domain.Notifications;
using Hiram.Domain.Routines;
using Hiram.Domain.Templates;
using Hiram.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.EndToEnd;

// The whole wire for the sms channel: POST /v1/events, outbox, event consumer, fan-out, the sms outbox
// row, the worker and SmsChannelDelivery. No carrier is involved and none is configured, which is the
// point: an unconfigured tenant settles as a permanent failure, so the run is deterministic offline and
// still proves the message travelled the entire path instead of being dropped at the fan-out.
[Collection("ApiHost")]
public class SmsEventDeliveryEndToEndTests : IAsyncLifetime
{
    private const string AdminKey = "admin-sms-e2e-key";
    private const string Phone = "+5511982254398";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:latest")
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8025))
        .Build();

    private WebApplicationFactory<Program>? _factory;
    private string _postgresConnection = string.Empty;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _mailpit.StartAsync());
        _postgresConnection = _postgres.GetConnectionString();

        Environment.SetEnvironmentVariable("ConnectionStrings__Hiram", _postgresConnection);
        Environment.SetEnvironmentVariable("Hiram__AdminKey", AdminKey);
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", "true");

        Environment.SetEnvironmentVariable("Hiram__Email__Platform__Provider", "smtp");
        Environment.SetEnvironmentVariable("Hiram__Email__Platform__Settings__host", _mailpit.Hostname);
        Environment.SetEnvironmentVariable("Hiram__Email__Platform__Settings__port", _mailpit.GetMappedPublicPort(1025).ToString());
        Environment.SetEnvironmentVariable("Hiram__Email__Platform__Settings__from", "no-reply@hiram.dev");
        Environment.SetEnvironmentVariable("Hiram__Email__Platform__Settings__security", "none");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(
            builder => builder.UseEnvironment("Development"));
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();

        foreach (var name in new[]
        {
            "ConnectionStrings__Hiram", "Hiram__AdminKey", "OTEL_SDK_DISABLED",
            "Hiram__Email__Platform__Provider", "Hiram__Email__Platform__Settings__host",
            "Hiram__Email__Platform__Settings__port", "Hiram__Email__Platform__Settings__from",
            "Hiram__Email__Platform__Settings__security"
        })
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        await Task.WhenAll(
            _postgres.DisposeAsync().AsTask(),
            _mailpit.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task EventOnBothChannels_DeliversTheEmail_AndCarriesTheSmsToTheProvider()
    {
        var (tenantId, client) = await NewTenantClient();
        var token = Guid.NewGuid().ToString("N");
        var subject = $"Pedido {token}";

        await SeedRoutine(tenantId, $"pedido-{token}", subject);

        var response = await client.PostAsJsonAsync("/v1/events", new
        {
            eventType = "pedido_enviado",
            eventId = $"evt-{token}",
            emissionSeq = 1L,
            recipient = new { email = "ops@example.com", phone = Phone },
            data = new { name = "Ada" }
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // Email is the proof the event was routed at all.
        var inbox = await WaitForMailpit(subject);
        Assert.Contains(inbox, message => message.Subject == subject);

        // SMS is the proof the second channel was not dropped at the fan-out: the notification exists,
        // it reached the worker, and the tenant has no carrier configured, so it settles permanently.
        var sms = await WaitForSmsNotification(client);
        Assert.Equal(Phone, sms.Recipient);
        Assert.Null(sms.Subject);

        var detail = await WaitForStatus(client, sms.Id, "dead_lettered");
        Assert.Equal("dead_lettered", detail.Status);

        var attempt = Assert.Single(detail.Attempts);
        Assert.Equal("permanent_failure", attempt.Outcome);
        Assert.False(attempt.Shadowed);
        Assert.Equal("sms", attempt.Provider);
        Assert.Equal("provider_not_configured", attempt.Error);
    }

    [Fact]
    public async Task EventWithoutAPhone_StillDeliversTheEmail_AndWritesNoSmsNotification()
    {
        var (tenantId, client) = await NewTenantClient();
        var token = Guid.NewGuid().ToString("N");
        var subject = $"Pedido {token}";

        await SeedRoutine(tenantId, $"pedido-{token}", subject);

        await client.PostAsJsonAsync("/v1/events", new
        {
            eventType = "pedido_enviado",
            eventId = $"evt-{token}",
            emissionSeq = 1L,
            recipient = new { email = "ops@example.com" },
            data = new { name = "Ada" }
        });

        // The email arriving is the barrier: once it lands the fan-out has already run for both channels,
        // so the absence of an sms notification is conclusive rather than a race.
        var inbox = await WaitForMailpit(subject);
        Assert.Contains(inbox, message => message.Subject == subject);

        var page = await client.GetFromJsonAsync<PageDto>("/v1/notifications?channel=sms");
        Assert.Empty(page!.Items);
    }

    private async Task SeedRoutine(Guid tenantId, string templateName, string subject)
    {
        await using var db = NewDb();

        var email = new Template(
            Guid.NewGuid(), tenantId, NotificationChannel.Email, templateName, subject, "Ola {{ name }}", DateTimeOffset.UtcNow);
        email.Approve();
        db.Set<Template>().Add(email);

        var sms = new Template(
            Guid.NewGuid(), tenantId, NotificationChannel.Sms, templateName, subject: null, "Ola {{ name }}, seu pedido saiu", DateTimeOffset.UtcNow);
        sms.Approve();
        db.Set<Template>().Add(sms);

        db.Routines.Add(new Routine(
            Guid.NewGuid(), tenantId, "pedido_enviado", templateName,
            [NotificationChannel.Email, NotificationChannel.Sms], NotificationCategory.Transactional, active: true));

        await db.SaveChangesAsync();
    }

    private HiramDbContext NewDb() =>
        new(new DbContextOptionsBuilder<HiramDbContext>().UseNpgsql(_postgresConnection).Options);

    private async Task<(Guid TenantId, HttpClient Client)> NewTenantClient()
    {
        var admin = _factory!.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Admin-Key", AdminKey);

        var tenantResponse = await admin.PostAsJsonAsync("/v1/admin/tenants", new { name = "sms-e2e", deliveryMode = "live" });
        tenantResponse.EnsureSuccessStatusCode();
        var tenant = (await tenantResponse.Content.ReadFromJsonAsync<TenantCreatedDto>())!;

        var keyResponse = await admin.PostAsJsonAsync("/v1/admin/api-keys", new { tenantId = tenant.Id, name = "server" });
        keyResponse.EnsureSuccessStatusCode();
        var key = (await keyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedDto>())!;

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key.Key);
        return (tenant.Id, client);
    }

    private static async Task<SummaryDto> WaitForSmsNotification(HttpClient client)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var page = await client.GetFromJsonAsync<PageDto>("/v1/notifications?channel=sms");
            if (page!.Items.Count > 0)
                return page.Items[0];
            await Task.Delay(500);
        }

        Assert.Fail("The sms notification was never written, so the fan-out dropped the channel.");
        return null!;
    }

    private static async Task<DetailDto> WaitForStatus(HttpClient client, Guid id, string status)
    {
        DetailDto? detail = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            detail = await client.GetFromJsonAsync<DetailDto>($"/v1/notifications/{id}");
            if (detail!.Status == status)
                break;
            await Task.Delay(500);
        }

        return detail!;
    }

    private async Task<List<MailpitMessage>> MailpitMessages()
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(8025)}") };
        var inbox = await http.GetFromJsonAsync<MailpitInbox>("/api/v1/messages");
        return inbox!.Messages;
    }

    private async Task<List<MailpitMessage>> WaitForMailpit(string subject)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var messages = await MailpitMessages();
            if (messages.Any(message => message.Subject == subject))
                return messages;
            await Task.Delay(500);
        }

        return await MailpitMessages();
    }

    private sealed record TenantCreatedDto(Guid Id, string Name, string DeliveryMode);

    private sealed record ApiKeyCreatedDto(Guid Id, Guid TenantId, string Name, string Key, string Prefix);

    private sealed record PageDto(List<SummaryDto> Items, string? NextCursor);

    private sealed record SummaryDto(Guid Id, string Channel, string Recipient, string? Subject, string Status);

    private sealed record DetailDto(Guid Id, string Status, List<AttemptDto> Attempts);

    private sealed record AttemptDto(int AttemptNumber, string Provider, string Outcome, string? Error, double DurationMs, bool Shadowed, string? PayloadHash);

    private sealed record MailpitInbox(List<MailpitMessage> Messages);

    private sealed record MailpitMessage(string Subject);
}
