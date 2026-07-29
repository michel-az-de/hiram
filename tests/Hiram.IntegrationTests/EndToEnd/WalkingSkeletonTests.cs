using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using Hiram.Contracts;
using Hiram.Domain.Outbox;
using Hiram.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.EndToEnd;

[Collection("ApiHost")]
public class WalkingSkeletonTests : IAsyncLifetime
{
    private const string AdminKey = "admin-e2e-key";
    private static readonly Guid DevTenant = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Hiram", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Hiram__AdminKey", AdminKey);
        // The manual ActivityListener below keeps spans alive for the assertion; the OTLP exporter has
        // nothing to talk to in tests, so disable the SDK to avoid export noise and shutdown delays.
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", "true");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(
            builder => builder.UseEnvironment("Development"));
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
    public async Task PostedNotification_FlowsThroughOutboxToSent_InASingleTrace()
    {
        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Hiram.Messaging" or "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add
        };
        ActivitySource.AddActivityListener(listener);

        var apiKey = await IssueApiKey();

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var response = await client.PostAsJsonAsync(
            "/v1/notifications",
            new SubmitNotificationRequest("email", "felipe@example.com", "hello", "first slice"));

        Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<NotificationAccepted>();
        Assert.NotNull(accepted);

        NotificationResponse? view = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            view = await client.GetFromJsonAsync<NotificationResponse>($"/v1/notifications/{accepted!.Id}");
            if (view?.Status == "sent")
                break;
            await Task.Delay(500);
        }

        Assert.NotNull(view);
        Assert.Equal("sent", view!.Status);

        var consume = SingleActivity(activities, "consume email");

        // The outbox preserves the request traceparent and the PostgreSQL worker resumes it directly.
        Assert.NotEqual(default, consume.ParentSpanId);
        Assert.Contains(activities, a =>
            a.Source.Name == "Microsoft.AspNetCore" && a.TraceId == consume.TraceId);
    }

    [Fact]
    public async Task UnsupportedOutboxType_IsDeadLetteredWithEvidence()
    {
        _ = _factory!.CreateClient();
        var messageId = Guid.NewGuid();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<HiramDbContext>();
            context.OutboxMessages.Add(new OutboxMessage(
                messageId,
                DevTenant,
                "unsupported",
                "{}",
                DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }

        OutboxMessage? stored = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<HiramDbContext>();
            stored = await context.OutboxMessages.AsNoTracking().SingleAsync(message => message.Id == messageId);
            if (stored.ProcessedAtUtc is not null)
                break;

            await Task.Delay(250);
        }

        Assert.NotNull(stored!.ProcessedAtUtc);
        Assert.Contains("not supported", stored.LastError);
        Assert.Null(stored.LeaseUntil);
    }

    private async Task<string> IssueApiKey()
    {
        var admin = _factory!.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Admin-Key", AdminKey);

        // Shadow mode reaches the 'sent' terminal state without a live provider target; the full live
        // path to Mailpit is exercised by the F1 end to end suite.
        var tenantResponse = await admin.PostAsJsonAsync("/v1/admin/tenants", new { name = "easystok", deliveryMode = "shadow" });
        tenantResponse.EnsureSuccessStatusCode();
        var tenant = await tenantResponse.Content.ReadFromJsonAsync<TenantCreatedDto>();

        var keyResponse = await admin.PostAsJsonAsync("/v1/admin/api-keys", new { tenantId = tenant!.Id, name = "server" });
        keyResponse.EnsureSuccessStatusCode();
        var key = await keyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedDto>();
        return key!.Key;
    }

    private static Activity SingleActivity(ConcurrentBag<Activity> activities, string displayName)
    {
        var activity = activities.FirstOrDefault(a => a.DisplayName == displayName);
        Assert.True(activity is not null, $"Expected an activity named '{displayName}' but none was recorded.");
        return activity!;
    }

    private sealed record TenantCreatedDto(Guid Id, string Name, string DeliveryMode);

    private sealed record ApiKeyCreatedDto(Guid Id, Guid TenantId, string Name, string Key, string Prefix);
}
