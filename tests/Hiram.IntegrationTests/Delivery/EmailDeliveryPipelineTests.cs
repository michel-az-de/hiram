using System.Text.Json;
using Hiram.Application.Abstractions;
using Hiram.Application.Delivery;
using Hiram.Application.Notifications;
using Hiram.Application.Tenancy;
using Hiram.Domain.Notifications;
using Hiram.Domain.Tenants;
using Hiram.Infrastructure.Messaging;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Retry;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Delivery;

public class EmailDeliveryPipelineTests : IAsyncLifetime
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

    private sealed class ScriptedProvider(string name, IEnumerable<SendOutcome> outcomes) : IEmailProvider
    {
        private readonly Queue<SendOutcome> _outcomes = new(outcomes);

        public string Name { get; } = name;
        public int Calls { get; private set; }

        public Task<SendOutcome> SendAsync(EmailMessage message, EmailProviderSettings settings, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_outcomes.Count > 0 ? _outcomes.Dequeue() : new SendOutcome.PermanentFailure("no more scripted outcomes"));
        }
    }

    private sealed class NoTenantConfig : ITenantProviderConfigStore
    {
        public Task<TenantProviderConfig?> FindAsync(Guid tenantId, NotificationChannel channel, CancellationToken cancellationToken) =>
            Task.FromResult<TenantProviderConfig?>(null);
    }

    private sealed class NoopProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string protectedValue) => protectedValue;
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
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

    private EmailNotificationProcessor BuildProcessor(HiramDbContext context, IEmailProvider provider)
    {
        var resolver = new EmailProviderResolver(
            new NoTenantConfig(),
            new NoopProtector(),
            new PlatformEmailDefaults("fake", null, new Dictionary<string, string>()),
            [provider]);

        return new EmailNotificationProcessor(context, resolver, FastPipeline(), new TestClock(), NullLogger<EmailNotificationProcessor>.Instance);
    }

    private async Task<(Guid TenantId, Guid NotificationId)> Seed(DeliveryMode deliveryMode)
    {
        var tenantId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        await using var context = NewContext();
        context.Tenants.Add(new Tenant(tenantId, "test-tenant", deliveryMode, DateTimeOffset.UtcNow));
        context.NotificationRequests.Add(
            new NotificationRequest(notificationId, tenantId, NotificationChannel.Email, "ops@example.com", "hello", "f1", DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
        return (tenantId, notificationId);
    }

    private static byte[] PayloadFor(Guid notificationId, Guid tenantId) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new OutboxNotificationPayload(notificationId, tenantId, "email", "ops@example.com", "hello", "f1"));

    private async Task<List<DeliveryAttempt>> AttemptsFor(Guid notificationId)
    {
        await using var context = NewContext();
        return await context.DeliveryAttempts
            .Where(a => a.NotificationId == notificationId)
            .OrderBy(a => a.AttemptNumber)
            .ToListAsync();
    }

    private async Task<NotificationStatus> StatusOf(Guid notificationId)
    {
        await using var context = NewContext();
        return (await context.NotificationRequests.FindAsync(notificationId))!.Status;
    }

    [Fact]
    public async Task Process_RecoversOnSecondAttempt_WhenFirstIsTransient()
    {
        var (tenantId, notificationId) = await Seed(DeliveryMode.Live);
        var provider = new ScriptedProvider("fake", [new SendOutcome.TransientFailure("temporary"), new SendOutcome.Sent()]);

        await using (var context = NewContext())
            await BuildProcessor(context, provider).ProcessAsync(PayloadFor(notificationId, tenantId), CancellationToken.None);

        Assert.Equal(NotificationStatus.Sent, await StatusOf(notificationId));

        var attempts = await AttemptsFor(notificationId);
        Assert.Equal(2, attempts.Count);
        Assert.Equal(DeliveryOutcome.TransientFailure, attempts[0].Outcome);
        Assert.Equal(DeliveryOutcome.Sent, attempts[1].Outcome);
    }

    [Fact]
    public async Task Process_FailsWithoutRetry_WhenPermanent()
    {
        var (tenantId, notificationId) = await Seed(DeliveryMode.Live);
        var provider = new ScriptedProvider("fake", [new SendOutcome.PermanentFailure("rejected")]);

        await using (var context = NewContext())
            await BuildProcessor(context, provider).ProcessAsync(PayloadFor(notificationId, tenantId), CancellationToken.None);

        Assert.Equal(NotificationStatus.Failed, await StatusOf(notificationId));
        Assert.Equal(1, provider.Calls);

        var attempts = await AttemptsFor(notificationId);
        Assert.Single(attempts);
        Assert.Equal(DeliveryOutcome.PermanentFailure, attempts[0].Outcome);
        Assert.Equal("rejected", attempts[0].Error);
    }

    [Fact]
    public async Task Process_FailsAfterThreeAttempts_WhenAlwaysTransient()
    {
        var (tenantId, notificationId) = await Seed(DeliveryMode.Live);
        var provider = new ScriptedProvider("fake", Enumerable.Repeat<SendOutcome>(new SendOutcome.TransientFailure("temporary"), 5));

        await using (var context = NewContext())
            await BuildProcessor(context, provider).ProcessAsync(PayloadFor(notificationId, tenantId), CancellationToken.None);

        Assert.Equal(NotificationStatus.Failed, await StatusOf(notificationId));
        Assert.Equal(3, provider.Calls);

        var attempts = await AttemptsFor(notificationId);
        Assert.Equal(3, attempts.Count);
        Assert.Equal(new[] { 1, 2, 3 }, attempts.Select(a => a.AttemptNumber).ToArray());
        Assert.All(attempts, a => Assert.Equal("fake", a.Provider));
        Assert.All(attempts, a => Assert.True(a.Duration >= TimeSpan.Zero));
    }

    [Fact]
    public async Task Process_RecordsShadowAttempt_WithoutCallingProvider_WhenTenantIsShadow()
    {
        var (tenantId, notificationId) = await Seed(DeliveryMode.Shadow);
        // Scripted to a hard failure: if the provider were ever called, the assertions below would catch it.
        var provider = new ScriptedProvider("fake", [new SendOutcome.PermanentFailure("must not be called")]);

        await using (var context = NewContext())
            await BuildProcessor(context, provider).ProcessAsync(PayloadFor(notificationId, tenantId), CancellationToken.None);

        Assert.Equal(0, provider.Calls);
        Assert.Equal(NotificationStatus.Sent, await StatusOf(notificationId));

        var attempts = await AttemptsFor(notificationId);
        Assert.Single(attempts);
        Assert.Equal(DeliveryOutcome.ShadowWouldSend, attempts[0].Outcome);
        Assert.True(attempts[0].Shadowed);
        Assert.Equal("fake", attempts[0].Provider);
        Assert.False(string.IsNullOrEmpty(attempts[0].PayloadHash));
    }
}
