using Hiram.Application.Delivery;
using Hiram.Application.Tenancy;
using Hiram.Domain.Notifications;
using Hiram.Domain.Tenants;

namespace Hiram.UnitTests.Delivery;

public class EmailProviderResolverTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private sealed class FakeProvider(string name) : IEmailProvider
    {
        public string Name { get; } = name;

        public Task<SendOutcome> SendAsync(EmailMessage message, EmailProviderSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult<SendOutcome>(new SendOutcome.Sent());
    }

    private sealed class FakeConfigStore(TenantProviderConfig? config) : ITenantProviderConfigStore
    {
        public Task<TenantProviderConfig?> FindAsync(Guid tenantId, NotificationChannel channel, CancellationToken cancellationToken) =>
            Task.FromResult(config);

        public Task UpsertAsync(TenantProviderConfig config, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeProtector : ISecretProtector
    {
        public int UnprotectCalls { get; private set; }

        public string Protect(string plaintext) => "protected:" + plaintext;

        public string Unprotect(string protectedValue)
        {
            UnprotectCalls++;
            return protectedValue.Replace("protected:", string.Empty);
        }
    }

    private static PlatformEmailDefaults Platform() =>
        new("smtp", "platform-secret", new Dictionary<string, string> { ["from"] = "no-reply@hiram.dev" });

    private static EmailProviderResolver Resolver(
        TenantProviderConfig? config,
        FakeProtector protector,
        params IEmailProvider[] providers) =>
        new(new FakeConfigStore(config), protector, Platform(), providers);

    [Fact]
    public async Task Resolve_UsesTenantConfig_AndDecryptsSecret()
    {
        var config = new TenantProviderConfig(
            Tenant, NotificationChannel.Email, "resend", "{\"from\":\"hi@easystok.com\"}", "protected:re_123", DateTimeOffset.UnixEpoch);
        var protector = new FakeProtector();
        var resolver = Resolver(config, protector, new FakeProvider("resend"), new FakeProvider("smtp"));

        var resolved = await resolver.ResolveAsync(Tenant, CancellationToken.None);

        Assert.Equal("resend", resolved.Provider.Name);
        Assert.Equal("re_123", resolved.Settings.Secret);
        Assert.Equal("hi@easystok.com", resolved.Settings.Values["from"]);
        Assert.Equal(1, protector.UnprotectCalls);
    }

    [Fact]
    public async Task Resolve_FallsBackToPlatform_WhenNoTenantConfig()
    {
        var resolver = Resolver(config: null, new FakeProtector(), new FakeProvider("smtp"));

        var resolved = await resolver.ResolveAsync(Tenant, CancellationToken.None);

        Assert.Equal("smtp", resolved.Provider.Name);
        Assert.Equal("platform-secret", resolved.Settings.Secret);
        Assert.Equal("no-reply@hiram.dev", resolved.Settings.Values["from"]);
    }

    [Fact]
    public async Task Resolve_LeavesSecretNull_WhenConfigHasNone()
    {
        var config = new TenantProviderConfig(
            Tenant, NotificationChannel.Email, "smtp", "{\"host\":\"localhost\"}", secretProtected: null, DateTimeOffset.UnixEpoch);
        var protector = new FakeProtector();
        var resolver = Resolver(config, protector, new FakeProvider("smtp"));

        var resolved = await resolver.ResolveAsync(Tenant, CancellationToken.None);

        Assert.Null(resolved.Settings.Secret);
        Assert.Equal(0, protector.UnprotectCalls);
    }

    [Fact]
    public async Task Resolve_Throws_WhenProviderIsNotRegistered()
    {
        var config = new TenantProviderConfig(
            Tenant, NotificationChannel.Email, "unknown", "{}", null, DateTimeOffset.UnixEpoch);
        var resolver = Resolver(config, new FakeProtector(), new FakeProvider("smtp"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(Tenant, CancellationToken.None));
    }
}
