using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Hiram.Contracts;
using Hiram.Dispatcher;
using Hiram.Infrastructure.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Hiram.IntegrationTests.EndToEnd;

public class WalkingSkeletonTests : IAsyncLifetime
{
    private const string ApiKey = "e2e-test-key";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management")
        .WithUsername("hiram")
        .WithPassword("hiram")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());

        var rabbitConnection = _rabbit.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__Hiram", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMq", rabbitConnection);
        Environment.SetEnvironmentVariable("Auth__DevApiKey", ApiKey);
        // The manual ActivityListener below keeps spans alive for the assertion; the OTLP exporter has
        // nothing to talk to in tests, so disable the SDK to avoid export noise and shutdown delays.
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", "true");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
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

        Environment.SetEnvironmentVariable("ConnectionStrings__Hiram", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMq", null);
        Environment.SetEnvironmentVariable("Auth__DevApiKey", null);
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", null);

        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
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

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);

        var response = await client.PostAsJsonAsync(
            "/v1/notifications",
            new SubmitNotificationRequest("email", "felipe@example.com", "hello", "first slice"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
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

        var publish = SingleActivity(activities, "publish email");
        var consume = SingleActivity(activities, "consume email");

        // Publish continues the request's trace and the consume continues the publish: one trace end to end.
        Assert.Equal(publish.TraceId, consume.TraceId);
        Assert.NotEqual(default, publish.ParentSpanId);
        Assert.Contains(activities, a =>
            a.Source.Name == "Microsoft.AspNetCore" && a.TraceId == publish.TraceId);
    }

    private static Activity SingleActivity(ConcurrentBag<Activity> activities, string displayName)
    {
        var activity = activities.FirstOrDefault(a => a.DisplayName == displayName);
        Assert.True(activity is not null, $"Expected an activity named '{displayName}' but none was recorded.");
        return activity!;
    }
}
