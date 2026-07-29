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
using Hiram.Domain.Routines;
using Hiram.Domain.Templates;
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
    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:latest")
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8025))
        .Build();

    private WebApplicationFactory<Program>? _factory;
    private string _postgresConnection = string.Empty;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync(), _mailpit.StartAsync());
        _postgresConnection = _postgres.GetConnectionString();

        var rabbitConnection = _rabbit.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__Hiram", _postgresConnection);
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMq", rabbitConnection);
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
                services.AddHostedService<EventConsumerWorker>();
                services.AddHostedService<PushConsumerWorker>();
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();

        foreach (var name in new[]
        {
            "ConnectionStrings__Hiram", "ConnectionStrings__RabbitMq", "Hiram__AdminKey",
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
    public async Task TemplatedSubmit_RendersAndDeliversToMailpit()
    {
        var (_, client) = await NewTenantClient("live");
        var token = Guid.NewGuid().ToString("N");
        var templateName = $"welcome-{token}";

        var create = await client.PostAsJsonAsync("/v1/templates",
            new { channel = "email", name = templateName, subject = $"Hi {{{{ name }}}} {token}", body = "Welcome {{ name }}" });
        create.EnsureSuccessStatusCode();

        var submit = await client.PostAsJsonAsync("/v1/notifications",
            new { channel = "email", recipient = "ops@example.com", template = templateName, data = new { name = "Ada" } });
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        var id = (await submit.Content.ReadFromJsonAsync<NotificationAccepted>())!.Id;

        var detail = await WaitForStatus(client, id, "sent");
        Assert.Equal("sent", detail.Status);

        var expectedSubject = $"Hi Ada {token}";
        var inbox = await WaitForMailpit(expectedSubject);
        Assert.Contains(inbox, message => message.Subject == expectedSubject);
    }

    [Fact]
    public async Task EventEmail_Ingested_ResolvesRoutine_Renders_DeliversToMailpit()
    {
        var (tenantId, client) = await NewTenantClient("live");
        var token = Guid.NewGuid().ToString("N");
        var templateName = $"order-{token}";

        // The routine engine resolves the event to this approved email template, renders it with the event
        // data and hands the message to the same email path the direct notifications use.
        await SeedApprovedEmailRoutine(tenantId, "produto_vencendo", templateName, $"Order {{{{ name }}}} {token}", "Body {{ name }}");

        var response = await client.PostAsJsonAsync("/v1/events", new
        {
            eventType = "produto_vencendo",
            eventId = $"evt-{token}",
            emissionSeq = 1L,
            recipient = new { email = "ops@example.com" },
            data = new { name = "Ada" }
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var expectedSubject = $"Order Ada {token}";
        var inbox = await WaitForMailpit(expectedSubject);
        Assert.Contains(inbox, message => message.Subject == expectedSubject);
    }

    [Fact]
    public async Task EventEmail_Marketing_WithoutConsent_IsSuppressed_NotDelivered()
    {
        var (tenantId, client) = await NewTenantClient("live");
        var token = Guid.NewGuid().ToString("N");
        var marketingSubject = $"Promo {token}";
        var barrierSubject = $"Aviso {token}";

        // Marketing has no legitimate-interest default, so an event without a RecipientUserId is suppressed.
        // A transactional routine on a second event is the barrier: once its email arrives, the event consumer
        // has already processed the marketing event, so the marketing email's absence is conclusive.
        await SeedApprovedEmailRoutine(tenantId, "promo_evento", $"promo-{token}", marketingSubject, "Corpo", NotificationCategory.Marketing);
        await SeedApprovedEmailRoutine(tenantId, "aviso_evento", $"aviso-{token}", barrierSubject, "Corpo");

        await client.PostAsJsonAsync("/v1/events", new
        {
            eventType = "promo_evento", eventId = $"promo-{token}", emissionSeq = 1L,
            recipient = new { email = "ops@example.com" }, data = new { }
        });
        await client.PostAsJsonAsync("/v1/events", new
        {
            eventType = "aviso_evento", eventId = $"aviso-{token}", emissionSeq = 1L,
            recipient = new { email = "ops@example.com" }, data = new { }
        });

        var inbox = await WaitForMailpit(barrierSubject);
        Assert.Contains(inbox, message => message.Subject == barrierSubject);
        Assert.DoesNotContain(inbox, message => message.Subject == marketingSubject);
    }

    [Fact]
    public async Task LevanteConfirmation_ViaAdminRoutinesEndpoint_DeliversToMailpit()
    {
        var (tenantId, client) = await NewTenantClient("live");
        var token = Guid.NewGuid().ToString("N");
        var templateName = $"newsletter-confirmacao-{token}";
        var subject = $"Confirme {token}";

        await CreateApprovedEmailTemplate(
            client, templateName, subject, "Para confirmar acesse: {{ confirmUrlBase }}?token={{ token }}");

        var routine = await CreateRoutineViaAdmin(tenantId, "assinatura_solicitada", templateName);
        Assert.Equal(HttpStatusCode.Created, routine.StatusCode);

        var response = await client.PostAsJsonAsync("/v1/events", new
        {
            eventType = "assinatura_solicitada",
            eventId = $"evt-{token}",
            emissionSeq = 1L,
            recipient = new { email = "prospect@example.com" },
            data = new { token, confirmUrlBase = "https://levante.test/newsletter/confirmar" }
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var inbox = await WaitForMailpit(subject);
        Assert.Contains(inbox, message => message.Subject == subject);
    }

    [Fact]
    public async Task AdminRoutines_SecondCreate_IsIdempotent()
    {
        var (tenantId, client) = await NewTenantClient("live");
        var token = Guid.NewGuid().ToString("N");
        var templateName = $"welcome-{token}";
        await CreateApprovedEmailTemplate(client, templateName, "Bem vindo", "Corpo");

        var first = await CreateRoutineViaAdmin(tenantId, "assinante_confirmado", templateName);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await CreateRoutineViaAdmin(tenantId, "assinante_confirmado", templateName);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        await using var db = NewDb();
        var count = await db.Routines.CountAsync(
            r => r.TenantId == tenantId && r.EventType == "assinante_confirmado" && r.Active);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Replay_SameLevanteEventId_IngestsOnce_DeliversOnce()
    {
        var (tenantId, client) = await NewTenantClient("live");
        var token = Guid.NewGuid().ToString("N");
        var templateName = $"replay-{token}";
        var subject = $"Replay {token}";
        await SeedApprovedEmailRoutine(tenantId, "assinatura_solicitada", templateName, subject, "Corpo");

        var eventId = $"evt-{token}";
        object body = new
        {
            eventType = "assinatura_solicitada",
            eventId,
            emissionSeq = 1L,
            recipient = new { email = "prospect@example.com" },
            data = new { token, confirmUrlBase = "https://levante.test/newsletter/confirmar" }
        };

        var first = await client.PostAsJsonAsync("/v1/events", body);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        var second = await client.PostAsJsonAsync("/v1/events", body);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.True(second.Headers.Contains("Idempotency-Replayed"));

        // Primary, deterministic assertion: the unique index collapses the replay to one event.
        await using var db = NewDb();
        var events = await db.Events.CountAsync(e => e.TenantId == tenantId && e.EventId == eventId);
        Assert.Equal(1, events);

        // Secondary: a single email reaches the inbox.
        var inbox = await WaitForMailpit(subject);
        Assert.Single(inbox, message => message.Subject == subject);
    }

    private HttpClient AdminClient()
    {
        var admin = _factory!.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Admin-Key", AdminKey);
        return admin;
    }

    private async Task CreateApprovedEmailTemplate(HttpClient client, string name, string subject, string body)
    {
        var create = await client.PostAsJsonAsync("/v1/templates", new { channel = "email", name, subject, body });
        create.EnsureSuccessStatusCode();
        var template = await create.Content.ReadFromJsonAsync<TemplateCreatedDto>();
        var approve = await client.PostAsync($"/v1/templates/{template!.Id}/approve", null);
        approve.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> CreateRoutineViaAdmin(Guid tenantId, string eventType, string templateName) =>
        await AdminClient().PostAsJsonAsync("/v1/admin/routines", new
        {
            tenantId,
            eventType,
            templateName,
            channels = new[] { "email" },
            category = "transactional",
            active = true
        });

    private async Task SeedApprovedEmailRoutine(
        Guid tenantId, string eventType, string templateName, string subject, string body,
        NotificationCategory category = NotificationCategory.Transactional)
    {
        await using var db = NewDb();

        var template = new Template(Guid.NewGuid(), tenantId, NotificationChannel.Email, templateName, subject, body, DateTimeOffset.UtcNow);
        template.Approve();
        db.Set<Template>().Add(template);

        db.Routines.Add(new Routine(
            Guid.NewGuid(), tenantId, eventType, templateName,
            new[] { NotificationChannel.Email }, category, active: true));

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PushToUnknownSubscription_DeadLettersThroughTheDispatcher()
    {
        var (_, client) = await NewTenantClient("live");

        // Channel push with a subscription id that was never registered flows through the push consumer and,
        // finding no subscription, settles as a permanent failure dead letter. This exercises the whole push path.
        var submit = await client.PostAsJsonAsync("/v1/notifications",
            new { channel = "push", recipient = Guid.NewGuid().ToString(), subject = "title", body = "body" });
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        var id = (await submit.Content.ReadFromJsonAsync<NotificationAccepted>())!.Id;

        var detail = await WaitForStatus(client, id, "dead_lettered");
        Assert.Equal("dead_lettered", detail.Status);
        Assert.Contains(detail.Attempts, attempt => attempt.Outcome == "permanent_failure");
    }

    [Fact]
    public async Task PoisonMessage_LandsInDeadLetterParkingLot()
    {
        // Creating a client boots the host, which applies the migrations before the direct DB write below.
        var (tenantId, _) = await NewTenantClient("live");
        // An outbox row pointing at a notification that never existed is deterministic poison once consumed.
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

    private sealed record TemplateCreatedDto(Guid Id);

    private sealed record ApiKeyCreatedDto(Guid Id, Guid TenantId, string Name, string Key, string Prefix);

    private sealed record DetailDto(Guid Id, string Status, List<AttemptDto> Attempts);

    private sealed record AttemptDto(int AttemptNumber, string Provider, string Outcome, string? Error, double DurationMs, bool Shadowed, string? PayloadHash);

    private sealed record MailpitInbox(List<MailpitMessage> Messages);

    private sealed record MailpitMessage(string Subject);
}
