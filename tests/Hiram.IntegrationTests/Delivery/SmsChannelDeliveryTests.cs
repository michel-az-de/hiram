using System.Text.Json;
using Hiram.Application.Delivery;
using Hiram.Application.Tenancy;
using Hiram.Domain.Notifications;
using Hiram.Domain.Tenants;
using Hiram.Infrastructure.Messaging;

namespace Hiram.IntegrationTests.Delivery;

public class SmsChannelDeliveryTests
{
    private sealed class StoredConfig(TenantProviderConfig? config) : ITenantProviderConfigStore
    {
        public Task<TenantProviderConfig?> FindAsync(Guid tenantId, NotificationChannel channel, CancellationToken cancellationToken) =>
            Task.FromResult(channel == NotificationChannel.Sms ? config : null);

        public Task UpsertAsync(TenantProviderConfig config, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoopProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string protectedValue) => "unprotected:" + protectedValue;
    }

    private sealed class FakeSmsProvider : ISmsProvider
    {
        public SmsProviderSettings? LastSettings { get; private set; }
        public SmsMessage? LastMessage { get; private set; }

        public string Name => "twilio-sms";

        public Task<SendOutcome> SendAsync(SmsMessage message, SmsProviderSettings settings, CancellationToken cancellationToken)
        {
            LastMessage = message;
            LastSettings = settings;
            return Task.FromResult<SendOutcome>(new SendOutcome.Sent("SM1"));
        }
    }

    private static NotificationRequest Notification(Guid tenantId) => new(
        Guid.NewGuid(), tenantId, NotificationChannel.Sms, "+5511982254398", subject: null, "corpo", DateTimeOffset.UnixEpoch);

    private static TenantProviderConfig Config(Guid tenantId, string provider = "twilio-sms") => new(
        tenantId,
        NotificationChannel.Sms,
        provider,
        JsonSerializer.Serialize(new Dictionary<string, string> { ["from"] = "+17372212163" }),
        "protected-secret",
        DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task Resolve_IsUnresolvable_WhenTheTenantHasNoSmsProvider()
    {
        var delivery = new SmsChannelDelivery(new StoredConfig(null), new NoopProtector(), [new FakeSmsProvider()]);

        var send = await delivery.ResolveAsync(Notification(Guid.NewGuid()), CancellationToken.None);

        // Unlike email there is no platform fallback, so this settles as a permanent failure that names
        // the cause instead of silently sending from someone else's account.
        var unresolved = Assert.IsType<UnresolvedSend>(send);
        var outcome = Assert.IsType<SendOutcome.PermanentFailure>(await unresolved.SendAsync(CancellationToken.None));
        Assert.Equal("provider_not_configured", outcome.Reason);
    }

    [Fact]
    public async Task Resolve_IsUnresolvable_WhenTheConfiguredProviderIsNotRegistered()
    {
        var tenantId = Guid.NewGuid();
        var delivery = new SmsChannelDelivery(
            new StoredConfig(Config(tenantId, "carrier-that-left")), new NoopProtector(), [new FakeSmsProvider()]);

        var send = await delivery.ResolveAsync(Notification(tenantId), CancellationToken.None);

        var unresolved = Assert.IsType<UnresolvedSend>(send);
        var outcome = Assert.IsType<SendOutcome.PermanentFailure>(await unresolved.SendAsync(CancellationToken.None));
        Assert.Equal("provider_not_registered", outcome.Reason);
    }

    [Fact]
    public async Task Resolve_PassesTheDecryptedSecretAndTheBody_ToTheProvider()
    {
        var tenantId = Guid.NewGuid();
        var provider = new FakeSmsProvider();
        var delivery = new SmsChannelDelivery(new StoredConfig(Config(tenantId)), new NoopProtector(), [provider]);

        var send = await delivery.ResolveAsync(Notification(tenantId), CancellationToken.None);
        var outcome = await send.SendAsync(CancellationToken.None);

        Assert.Equal("twilio-sms", send.Provider);
        Assert.Equal("SM1", Assert.IsType<SendOutcome.Sent>(outcome).ProviderMessageId);
        Assert.Equal("+5511982254398", provider.LastMessage!.Recipient);
        Assert.Equal("corpo", provider.LastMessage.Body);
        Assert.Equal("unprotected:protected-secret", provider.LastSettings!.Secret);
        Assert.Equal("+17372212163", provider.LastSettings.Values["from"]);
    }
}
