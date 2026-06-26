using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using Hiram.Application.Delivery;
using Hiram.Application.Push;
using Hiram.Domain.Push;
using Hiram.Infrastructure.Push;

namespace Hiram.IntegrationTests.Push;

public class WebPushSenderTests
{
    private static readonly PushVapidOptions Vapid = BuildVapid();

    private static PushVapidOptions BuildVapid()
    {
        var keys = WebPush.VapidHelper.GenerateVapidKeys();
        return new PushVapidOptions("mailto:test@hiram.local", keys.PublicKey, keys.PrivateKey);
    }

    private static PushSubscription NewSubscription()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdh.ExportParameters(false);
        var point = new byte[65];
        point[0] = 0x04;
        parameters.Q.X!.CopyTo(point, 1);
        parameters.Q.Y!.CopyTo(point, 33);

        var p256dh = Base64Url.EncodeToString(point);
        var auth = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        return new PushSubscription(Guid.NewGuid(), Guid.NewGuid(), "https://push.example.com/abc", p256dh, auth, DateTimeOffset.UtcNow);
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private static WebPushSender SenderReturning(HttpStatusCode status, PushVapidOptions? options = null) =>
        new(new HttpClient(new StubHandler(status)), options ?? Vapid);

    [Fact]
    public async Task Send_ReturnsSent_OnSuccess()
    {
        var outcome = await SenderReturning(HttpStatusCode.Created)
            .SendAsync(NewSubscription(), "{\"title\":\"hi\"}", CancellationToken.None);

        Assert.IsType<SendOutcome.Sent>(outcome);
    }

    [Fact]
    public async Task Send_ReturnsPermanent_WhenSubscriptionGone()
    {
        var outcome = await SenderReturning(HttpStatusCode.Gone)
            .SendAsync(NewSubscription(), "{\"title\":\"hi\"}", CancellationToken.None);

        Assert.IsType<SendOutcome.PermanentFailure>(outcome);
    }

    [Fact]
    public async Task Send_ReturnsTransient_WhenThrottled()
    {
        var outcome = await SenderReturning(HttpStatusCode.TooManyRequests)
            .SendAsync(NewSubscription(), "{\"title\":\"hi\"}", CancellationToken.None);

        Assert.IsType<SendOutcome.TransientFailure>(outcome);
    }

    [Fact]
    public async Task Send_ReturnsPermanent_WhenNotConfigured()
    {
        var sender = SenderReturning(HttpStatusCode.Created, new PushVapidOptions("mailto:x", "", ""));

        var outcome = await sender.SendAsync(NewSubscription(), "{}", CancellationToken.None);

        Assert.IsType<SendOutcome.PermanentFailure>(outcome);
    }
}
