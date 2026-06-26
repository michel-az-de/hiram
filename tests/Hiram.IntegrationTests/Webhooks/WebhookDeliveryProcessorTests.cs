using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hiram.Application.Delivery;
using Hiram.Application.Tenancy;
using Hiram.Application.Webhooks;
using Hiram.Domain.Webhooks;
using Hiram.Infrastructure.Messaging;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Retry;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Webhooks;

public class WebhookDeliveryProcessorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private HiramDbContext NewContext() =>
        new(new DbContextOptionsBuilder<HiramDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private sealed class NoopProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string protectedValue) => protectedValue;
    }

    private sealed class CapturingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Signature { get; private set; }
        public byte[]? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Signature = request.Headers.TryGetValues("X-Hiram-Signature", out var values) ? values.First() : null;
            Body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            return new HttpResponseMessage(status);
        }
    }

    private static ResiliencePipeline<SendOutcome> FastPipeline() =>
        new ResiliencePipelineBuilder<SendOutcome>()
            .AddRetry(new RetryStrategyOptions<SendOutcome>
            {
                ShouldHandle = new PredicateBuilder<SendOutcome>().HandleResult(outcome => outcome is SendOutcome.TransientFailure),
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(1),
                BackoffType = DelayBackoffType.Constant,
                UseJitter = false
            })
            .Build();

    private WebhookDeliveryProcessor BuildProcessor(HiramDbContext context, HttpMessageHandler handler) =>
        new(new HttpClient(handler), new WebhookEndpointStore(context), new NoopProtector(), FastPipeline(),
            NullLogger<WebhookDeliveryProcessor>.Instance);

    private async Task<Guid> SeedEndpoint(string url, string secret)
    {
        var tenantId = Guid.NewGuid();
        await using var context = NewContext();
        context.WebhookEndpoints.Add(new WebhookEndpoint(Guid.NewGuid(), tenantId, url, secret, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
        return tenantId;
    }

    private static byte[] PayloadFor(Guid tenantId) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new WebhookOutboxPayload(tenantId, Guid.NewGuid(), "email", "sent", DateTimeOffset.UtcNow));

    [Fact]
    public async Task Delivers_WithValidSignatureOverTheBody()
    {
        var tenantId = await SeedEndpoint("https://t.example.com/h", "mysecret");
        var handler = new CapturingHandler(HttpStatusCode.OK);

        await using (var context = NewContext())
            await BuildProcessor(context, handler).ProcessAsync(PayloadFor(tenantId), CancellationToken.None);

        Assert.Equal(1, handler.Calls);
        Assert.NotNull(handler.Body);
        var expected = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("mysecret"), handler.Body!));
        Assert.Equal(expected, handler.Signature);
    }

    [Fact]
    public async Task Retries_OnServerError()
    {
        var tenantId = await SeedEndpoint("https://t.example.com/h", "s");
        var handler = new CapturingHandler(HttpStatusCode.InternalServerError);

        await using (var context = NewContext())
            await BuildProcessor(context, handler).ProcessAsync(PayloadFor(tenantId), CancellationToken.None);

        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task DoesNotRetry_OnClientError()
    {
        var tenantId = await SeedEndpoint("https://t.example.com/h", "s");
        var handler = new CapturingHandler(HttpStatusCode.BadRequest);

        await using (var context = NewContext())
            await BuildProcessor(context, handler).ProcessAsync(PayloadFor(tenantId), CancellationToken.None);

        Assert.Equal(1, handler.Calls);
    }
}
