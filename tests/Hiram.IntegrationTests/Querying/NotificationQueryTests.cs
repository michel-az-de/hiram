using System.Net;
using System.Net.Http.Json;
using Hiram.Contracts;
using Hiram.Domain.DeadLetters;
using Hiram.Domain.Notifications;
using Hiram.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Querying;

[Collection("ApiHost")]
public class NotificationQueryTests : IAsyncLifetime
{
    private const string AdminKey = "admin-query-key";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private WebApplicationFactory<Program>? _factory;
    private string _postgresConnection = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _postgresConnection = _postgres.GetConnectionString();

        Environment.SetEnvironmentVariable("ConnectionStrings__Hiram", _postgresConnection);
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

    private HiramDbContext NewDb() =>
        new(new DbContextOptionsBuilder<HiramDbContext>().UseNpgsql(_postgresConnection).Options);

    [Fact]
    public async Task List_PaginatesStablyAcrossPages()
    {
        var (_, client) = await NewTenantClient();
        var posted = new List<Guid>();
        for (var i = 0; i < 5; i++)
            posted.Add(await Post(client, $"msg-{i}"));

        var collected = new List<Guid>();
        string? cursor = null;
        for (var guard = 0; guard < 10; guard++)
        {
            var url = "/v1/notifications?limit=2" + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = await client.GetFromJsonAsync<PageDto>(url);
            collected.AddRange(page!.Items.Select(item => item.Id));
            cursor = page.NextCursor;
            if (cursor is null)
                break;
        }

        Assert.Equal(5, collected.Count);
        Assert.Equal(5, collected.Distinct().Count());
        Assert.Equal(posted.ToHashSet(), collected.ToHashSet());
    }

    [Fact]
    public async Task List_AndDetailAreScopedToTenant()
    {
        var (_, clientA) = await NewTenantClient();
        var (_, clientB) = await NewTenantClient();
        var idA = await Post(clientA, "tenant-a");
        var idB = await Post(clientB, "tenant-b");

        var pageA = await clientA.GetFromJsonAsync<PageDto>("/v1/notifications?limit=100");
        Assert.Contains(pageA!.Items, item => item.Id == idA);
        Assert.DoesNotContain(pageA.Items, item => item.Id == idB);

        var crossTenant = await clientA.GetAsync($"/v1/notifications/{idB}");
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);
    }

    [Fact]
    public async Task List_FiltersByStatus()
    {
        var (_, client) = await NewTenantClient();
        var id = await Post(client, "filter-me");

        var accepted = await client.GetFromJsonAsync<PageDto>("/v1/notifications?status=accepted&limit=100");
        Assert.Contains(accepted!.Items, item => item.Id == id);

        var sent = await client.GetFromJsonAsync<PageDto>("/v1/notifications?status=sent&limit=100");
        Assert.DoesNotContain(sent!.Items, item => item.Id == id);
    }

    [Fact]
    public async Task Detail_IncludesDeliveryAttempts()
    {
        var (tenantId, client) = await NewTenantClient();
        var id = await Post(client, "with-attempts");

        await using (var db = NewDb())
        {
            db.DeliveryAttempts.Add(new DeliveryAttempt(
                Guid.NewGuid(), tenantId, id, 1, "smtp", DeliveryOutcome.TransientFailure, "temporary", TimeSpan.FromMilliseconds(12), DateTimeOffset.UtcNow));
            db.DeliveryAttempts.Add(new DeliveryAttempt(
                Guid.NewGuid(), tenantId, id, 2, "smtp", DeliveryOutcome.Sent, null, TimeSpan.FromMilliseconds(8), DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var detail = await client.GetFromJsonAsync<DetailDto>($"/v1/notifications/{id}");

        Assert.Equal(2, detail!.Attempts.Count);
        Assert.Equal("transient_failure", detail.Attempts[0].Outcome);
        Assert.Equal("sent", detail.Attempts[1].Outcome);
        Assert.Equal(1, detail.Attempts[0].AttemptNumber);
    }

    [Fact]
    public async Task List_FiltersByDeadLettered()
    {
        var (_, client) = await NewTenantClient();
        var id = await Post(client, "to-dead-letter");

        await using (var db = NewDb())
        {
            var notification = await db.NotificationRequests.FindAsync(id);
            notification!.MarkSending();
            notification.MarkDeadLettered();
            await db.SaveChangesAsync();
        }

        var deadLettered = await client.GetFromJsonAsync<PageDto>("/v1/notifications?status=dead_lettered&limit=100");
        Assert.Contains(deadLettered!.Items, item => item.Id == id);

        var sent = await client.GetFromJsonAsync<PageDto>("/v1/notifications?status=sent&limit=100");
        Assert.DoesNotContain(sent!.Items, item => item.Id == id);
    }

    [Fact]
    public async Task Detail_IncludesDeadLetter_WhenPresent()
    {
        var (tenantId, client) = await NewTenantClient();
        var id = await Post(client, "with-dead-letter");

        await using (var db = NewDb())
        {
            db.DeadLetterMessages.Add(new DeadLetterMessage(
                Guid.NewGuid(), tenantId, id, NotificationChannel.Email,
                "{\"x\":1}", "permanent_failure:rejected", 1, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var detail = await client.GetFromJsonAsync<DetailDto>($"/v1/notifications/{id}");

        Assert.NotNull(detail!.DeadLetter);
        Assert.Equal("permanent_failure:rejected", detail.DeadLetter!.Reason);
        Assert.Equal(1, detail.DeadLetter.AttemptCount);
        Assert.Null(detail.DeadLetter.ReplayedAtUtc);
    }

    private async Task<(Guid TenantId, HttpClient Client)> NewTenantClient()
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
        return (tenant.Id, client);
    }

    private static async Task<Guid> Post(HttpClient client, string subject)
    {
        var response = await client.PostAsJsonAsync("/v1/notifications", new SubmitNotificationRequest("email", "ops@example.com", subject, "body"));
        response.EnsureSuccessStatusCode();
        var accepted = await response.Content.ReadFromJsonAsync<NotificationAccepted>();
        return accepted!.Id;
    }

    private sealed record TenantCreatedDto(Guid Id, string Name, string DeliveryMode);

    private sealed record ApiKeyCreatedDto(Guid Id, Guid TenantId, string Name, string Key, string Prefix);

    private sealed record PageDto(List<SummaryDto> Items, string? NextCursor);

    private sealed record SummaryDto(Guid Id, string Channel, string Recipient, string Subject, string Status, DateTimeOffset CreatedAtUtc);

    private sealed record DetailDto(Guid Id, string Status, List<AttemptDto> Attempts, DeadLetterDto? DeadLetter = null);

    private sealed record AttemptDto(int AttemptNumber, string Provider, string Outcome, string? Error, double DurationMs, bool Shadowed, string? PayloadHash);

    private sealed record DeadLetterDto(string Reason, int AttemptCount, DateTimeOffset CreatedAtUtc, DateTimeOffset? ReplayedAtUtc);
}
