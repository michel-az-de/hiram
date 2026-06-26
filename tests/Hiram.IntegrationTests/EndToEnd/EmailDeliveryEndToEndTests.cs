using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Hiram.Application.Notifications;
using Hiram.Contracts;
using Hiram.Dispatcher;
using Hiram.Domain.Notifications;
using Hiram.Domain.Outbox;
using Hiram.Domain.Tenants;
using Hiram.Infrastructure;
using Hiram.Infrastructure.Messaging;
using Hiram.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace Hiram.IntegrationTests.EndToEnd;

[Collection("ApiHost")]
public class EmailDeliveryEndToEndTests : IAsyncLifetime
{
    private const string AdminKey = "admin-f1-e2e-key";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management")
        .WithUsername("hiram")
        .WithPassword("hiram")
        .Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();
    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:latest")
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8025))
        .Build();

    private WebApplicationFactory<Program>? _factory;
    private string _postgresConnection = string.Empty;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync(), _redis.StartAsync(), _mailpit.StartAsync());
        _postgresConnection = _postgres.GetConnectionString();

        var rabbitConnection = _rabbit.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__Hiram", _postgresConnection);
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMq", rabbitConnection);
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", _redis.GetConnectionString());
        Environment.SetEnvironmentVariable("Hiram__AdminKey", AdminKey);
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", "true");

        // The platform default SMTP provider points at Mailpit, so live tenants deliver there.
        Environment.SetEnvironmentVariable("Hiram__Email__Platform__Provider", "smtp");
        Environment.SetEnvironmentVariable("Hiram__Email__Platform__Settings__host", _mailpit.Hostname);
        Environment.SetEnvironmentVariable("Hiram__Email__Platform__Settings__port", _mailpit.GetMappedPublicPort(1025).ToString());
        Environment.SetEnvironmentVariable("Hiram__Email__Platform__Settings__from", "no-reply@hiram.dev");
        Environment.SetEnvironmentVariable("Hiram__Email__Platform__Settings__security", "none");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices((context, services) =>
            {
                services.AddHiramEmailDelivery(context.Configuration);
                services.AddHiramMessaging(rabbitConnection);
                services.AddHostedService<OutboxRelayWorker>();
                services.AddHostedService<EmailConsumerWorker>();
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();

        foreach (var name in new[]
        {
            "ConnectionStrings__Hiram", "ConnectionStrings__RabbitMq", "ConnectionStrings__Redis", "Hiram__AdminKey",
            "OTEL_SDK_DISABLED", "Hiram__Email__Platform__Provider", "Hiram__Email__Platform__Settings__host",
            "Hiram__Email__Platform__Settings__port", "Hiram__Email__Platform__Settings__from",
            "Hiram__Email__Platform__Settings__security"
        })
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        await Task.WhenAll(
            _postgres.DisposeAsync().AsTask(),
            _rabbit.DisposeAsync().AsTask(),
            _redis.DisposeAsync().AsTask(),
            _mailpit.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Live_DeliversToMailpit_AndRecordsSentAttempt()
    {
        var (_, client) = await NewTenantClient("live");
        var subject = $"live-{Guid.NewGuid():N}";

        var id = await PostAccepted(client, "ops@example.com", subject);
        var detail = await WaitForStatus(client, id, "sent");

        Assert.Equal("sent", detail.Status);
        Assert.Contains(detail.Attempts, attempt => attempt.Outcome == "sent" && !attempt.Shadowed);

        var inbox = await WaitForMailpit(subject);
        Assert.Contains(inbox, message => message.Subject == subject);
    }

    [Fact]
    public async Task Shadow_RecordsWouldSend_WithoutTouchingMailpit()
    {
        var (_, client) = await NewTenantClient("shadow");
        var subject = $"shadow-{Guid.NewGuid():N}";

        var id = await PostAccepted(client, "ops@example.com", subject);
        var detail = await WaitForStatus(client, id, "sent");

        Assert.Equal("sent", detail.Status);
        var attempt = Assert.Single(detail.Attempts);
        Assert.Equal("shadow_would_send", attempt.Outcome);
        Assert.True(attempt.Shadowed);
        Assert.False(string.IsNullOrEmpty(attempt.PayloadHash));

        var inbox = await MailpitMessages();
        Assert.DoesNotContain(inbox, message => message.Subject == subject);
    }

    [Fact]
    public async Task RepeatedIdempotencyKey_ReturnsSameNotificationOnce()
    {
        var (_, client) = await NewTenantClient("live");
        var key = $"evt-{Guid.NewGuid():N}";
        var subject = $"idem-{key}";

        var first = await Post(client, "ops@example.com", subject, key);
        var second = await Post(client, "ops@example.com", subject, key);

        var firstId = (await first.Content.ReadFromJsonAsync<NotificationAccepted>())!.Id;
        var secondId = (await second.Content.ReadFromJsonAsync<NotificationAccepted>())!.Id;

        Assert.Equal(firstId, secondId);
        Assert.False(first.Headers.Contains("Idempotency-Replayed"));
        Assert.True(second.Headers.TryGetValues("Idempotency-Replayed", out var values) && values.Contains("true"));
    }

    [Fact]
    public async Task PermanentFailure_DeadLetters_WithSinglePermanentAttempt()
    {
        var (tenantId, client) = await NewTenantClient("live");

        // A per-tenant SMTP config without a port makes the provider fail permanently on the first try.
        await using (var db = NewDb())
        {
            db.TenantProviderConfigs.Add(new TenantProviderConfig(
                tenantId,
                NotificationChannel.Email,
                "smtp",
                "{\"host\":\"localhost\",\"from\":\"no-reply@hiram.dev\",\"security\":\"none\"}",
                secretProtected: null,
                DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var id = await PostAccepted(client, "ops@example.com", $"fail-{Guid.NewGuid():N}");
        var detail = await WaitForStatus(client, id, "dead_lettered");

        Assert.Equal("dead_lettered", detail.Status);
        var attempt = Assert.Single(detail.Attempts);
        Assert.Equal("permanent_failure", attempt.Outcome);
        Assert.False(attempt.Shadowed);
    }

    [Fact]
    public async Task Replay_RedeliversDeadLetteredNotification_ToMailpit()
    {
        var (tenantId, client) = await NewTenantClient("live");
        var subject = $"replay-{Guid.NewGuid():N}";

        // A per-tenant SMTP config pointing nowhere makes the first delivery fail and dead letter.
        await using (var db = NewDb())
        {
            db.TenantProviderConfigs.Add(new TenantProviderConfig(
                tenantId,
                NotificationChannel.Email,
                "smtp",
                "{\"host\":\"localhost\",\"from\":\"no-reply@hiram.dev\",\"security\":\"none\"}",
                secretProtected: null,
                DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var id = await PostAccepted(client, "ops@example.com", subject);
        await WaitForStatus(client, id, "dead_lettered");

        // Drop the broken config so the resolver falls back to the platform default that points at Mailpit.
        await using (var db = NewDb())
        {
            await db.TenantProviderConfigs
                .Where(c => c.TenantId == tenantId && c.Channel == NotificationChannel.Email)
                .ExecuteDeleteAsync();
        }

        var replay = await client.PostAsync($"/v1/notifications/{id}/replay", content: null);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);

        var detail = await WaitForStatus(client, id, "sent");
        Assert.Equal("sent", detail.Status);

        var inbox = await WaitForMailpit(subject);
        Assert.Contains(inbox, message => message.Subject == subject);
    }

    [Fact]
    public async Task PoisonMessage_LandsInDeadLetterParkingLot()
    {
        // An outbox row pointing at a notification that never existed is deterministic poison once consumed.
        var tenantId = Guid.NewGuid();
        var missingNotificationId = Guid.NewGuid();

        await using (var db = NewDb())
        {
            db.OutboxMessages.Add(new OutboxMessage(
                Guid.NewGuid(), tenantId, HiramTopology.EmailRoutingKey,
                JsonSerializer.Serialize(new OutboxNotificationPayload(
                    missingNotificationId, tenantId, "email", "ops@example.com", "poison", "body")),
                DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        Assert.True(await WaitForParkedMessageAsync());
    }

    private async Task<bool> WaitForParkedMessageAsync()
    {
        await using var connection = new RabbitMqConnection(_rabbit.GetConnectionString(), NullLogger<RabbitMqConnection>.Instance);
        var rabbit = await connection.GetConnectionAsync(CancellationToken.None);
        await using var channel = await rabbit.CreateChannelAsync();

        for (var attempt = 0; attempt < 50; attempt++)
        {
            var result = await channel.BasicGetAsync(HiramTopology.DeadLetterQueue, autoAck: true);
            if (result is not null)
                return true;

            await Task.Delay(100);
        }

        return false;
    }

    private HiramDbContext NewDb() =>
        new(new DbContextOptionsBuilder<HiramDbContext>().UseNpgsql(_postgresConnection).Options);

    private async Task<(Guid TenantId, HttpClient Client)> NewTenantClient(string deliveryMode)
    {
        var admin = _factory!.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Admin-Key", AdminKey);

        var tenantResponse = await admin.PostAsJsonAsync("/v1/admin/tenants", new { name = "easystok", deliveryMode });
        tenantResponse.EnsureSuccessStatusCode();
        var tenant = await tenantResponse.Content.ReadFromJsonAsync<TenantCreatedDto>();

        var keyResponse = await admin.PostAsJsonAsync("/v1/admin/api-keys", new { tenantId = tenant!.Id, name = "server" });
        keyResponse.EnsureSuccessStatusCode();
        var key = await keyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedDto>();

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key!.Key);
        return (tenant.Id, client);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string recipient, string subject, string? idempotencyKey = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/notifications")
        {
            Content = JsonContent.Create(new SubmitNotificationRequest("email", recipient, subject, "f1 body"))
        };
        if (idempotencyKey is not null)
            message.Headers.Add("Idempotency-Key", idempotencyKey);

        return await client.SendAsync(message);
    }

    private static async Task<Guid> PostAccepted(HttpClient client, string recipient, string subject)
    {
        var response = await Post(client, recipient, subject);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NotificationAccepted>())!.Id;
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
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var messages = await MailpitMessages();
            if (messages.Any(message => message.Subject == subject))
                return messages;
            await Task.Delay(200);
        }

        return await MailpitMessages();
    }

    private sealed record TenantCreatedDto(Guid Id, string Name, string DeliveryMode);

    private sealed record ApiKeyCreatedDto(Guid Id, Guid TenantId, string Name, string Key, string Prefix);

    private sealed record DetailDto(Guid Id, string Status, List<AttemptDto> Attempts);

    private sealed record AttemptDto(int AttemptNumber, string Provider, string Outcome, string? Error, double DurationMs, bool Shadowed, string? PayloadHash);

    private sealed record MailpitInbox(List<MailpitMessage> Messages);

    private sealed record MailpitMessage(string Subject);
}
