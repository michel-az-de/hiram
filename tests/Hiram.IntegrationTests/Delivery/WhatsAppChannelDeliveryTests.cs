using System.Text.Json;
using Hiram.Application.Delivery;
using Hiram.Application.Tenancy;
using Hiram.Domain.Notifications;
using Hiram.Domain.Tenants;
using Hiram.Infrastructure.Messaging;

namespace Hiram.IntegrationTests.Delivery;

public class WhatsAppChannelDeliveryTests
{
    private sealed class StoredConfig(TenantProviderConfig? config) : ITenantProviderConfigStore
    {
        public Task<TenantProviderConfig?> FindAsync(Guid tenantId, NotificationChannel channel, CancellationToken cancellationToken) =>
            Task.FromResult(channel == NotificationChannel.WhatsApp ? config : null);

        public Task UpsertAsync(TenantProviderConfig config, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoopProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string protectedValue) => "unprotected:" + protectedValue;
    }

    private sealed class FakeWhatsAppProvider : IWhatsAppProvider
    {
        public WhatsAppProviderSettings? LastSettings { get; private set; }
        public WhatsAppMessage? LastMessage { get; private set; }

        public string Name => "twilio-whatsapp";

        public Task<SendOutcome> SendAsync(WhatsAppMessage message, WhatsAppProviderSettings settings, CancellationToken cancellationToken)
        {
            LastMessage = message;
            LastSettings = settings;
            return Task.FromResult<SendOutcome>(new SendOutcome.Sent("SM1"));
        }
    }

    private static NotificationRequest Notification(Guid tenantId) => new(
        Guid.NewGuid(), tenantId, NotificationChannel.WhatsApp, "+5511982254398", subject: null, "corpo", DateTimeOffset.UnixEpoch);

    private static TenantProviderConfig Config(Guid tenantId, string provider = "twilio-whatsapp") => new(
        tenantId,
        NotificationChannel.WhatsApp,
        provider,
        JsonSerializer.Serialize(new Dictionary<string, string> { ["from"] = "+14155238886" }),
        "protected-secret",
        DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task Resolve_IsUnresolvable_WhenTheTenantHasNoWhatsAppProvider()
    {
        var delivery = new WhatsAppChannelDelivery(new StoredConfig(null), new NoopProtector(), [new FakeWhatsAppProvider()]);

        var send = await delivery.ResolveAsync(Notification(Guid.NewGuid()), CancellationToken.None);

        // Unlike email there is no platform fallback, so this settles as a permanent failure that names
        // the cause instead of silently sending from someone else's sender.
        var unresolved = Assert.IsType<UnresolvedSend>(send);
        var outcome = Assert.IsType<SendOutcome.PermanentFailure>(await unresolved.SendAsync(CancellationToken.None));
        Assert.Equal("provider_not_configured", outcome.Reason);
    }

    [Fact]
    public async Task Resolve_IsUnresolvable_WhenTheConfiguredProviderIsNotRegistered()
    {
        var tenantId = Guid.NewGuid();
        var delivery = new WhatsAppChannelDelivery(
            new StoredConfig(Config(tenantId, "bsp-that-left")), new NoopProtector(), [new FakeWhatsAppProvider()]);

        var send = await delivery.ResolveAsync(Notification(tenantId), CancellationToken.None);

        var unresolved = Assert.IsType<UnresolvedSend>(send);
        var outcome = Assert.IsType<SendOutcome.PermanentFailure>(await unresolved.SendAsync(CancellationToken.None));
        Assert.Equal("provider_not_registered", outcome.Reason);
    }

    [Fact]
    public async Task Resolve_PassesTheDecryptedSecretAndTheBareRecipient_ToTheProvider()
    {
        var tenantId = Guid.NewGuid();
        var provider = new FakeWhatsAppProvider();
        var delivery = new WhatsAppChannelDelivery(new StoredConfig(Config(tenantId)), new NoopProtector(), [provider]);

        var send = await delivery.ResolveAsync(Notification(tenantId), CancellationToken.None);
        var outcome = await send.SendAsync(CancellationToken.None);

        Assert.Equal("twilio-whatsapp", send.Provider);
        Assert.Equal("SM1", Assert.IsType<SendOutcome.Sent>(outcome).ProviderMessageId);

        // The "whatsapp:" prefix is the adapter's business. What crosses this boundary is the number as
        // it was stored, so the resolver and the adapter cannot end up prefixing it twice.
        Assert.Equal("+5511982254398", provider.LastMessage!.Recipient);

        // Still free form after the shape opened up: turning a notification into an approved template is a
        // later slice, and this assertion is what would catch that flipping by accident.
        Assert.Equal("corpo", Assert.IsType<WhatsAppMessage.FreeForm>(provider.LastMessage).Body);
        Assert.Equal("unprotected:protected-secret", provider.LastSettings!.Secret);
        Assert.Equal("+14155238886", provider.LastSettings.Values["from"]);
    }
}
