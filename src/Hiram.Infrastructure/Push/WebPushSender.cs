using System.Net;
using Hiram.Application.Delivery;
using Hiram.Application.Push;
using WebPush;
using DomainSubscription = Hiram.Domain.Push.PushSubscription;

namespace Hiram.Infrastructure.Push;

public sealed class WebPushSender : IPushSender
{
    private readonly WebPushClient _client;
    private readonly PushVapidOptions _options;

    public WebPushSender(HttpClient httpClient, PushVapidOptions options)
    {
        _client = new WebPushClient(httpClient);
        _options = options;
    }

    public async Task<SendOutcome> SendAsync(DomainSubscription subscription, string payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.PublicKey) || string.IsNullOrEmpty(_options.PrivateKey))
            return new SendOutcome.PermanentFailure("Web push is not configured on this deployment.");

        var vapid = new VapidDetails(_options.Subject, _options.PublicKey, _options.PrivateKey);
        var target = new PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth);

        try
        {
            // The library call does not take a token, so WaitAsync enforces the per attempt timeout the caller set.
            await _client.SendNotificationAsync(target, payload, vapid).WaitAsync(cancellationToken);
            return new SendOutcome.Sent();
        }
        catch (WebPushException ex)
        {
            // A dead subscription answers 404 or 410 and will never accept a retry; everything else can be transient.
            return ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone
                ? new SendOutcome.PermanentFailure($"Push endpoint returned {(int)ex.StatusCode}.")
                : new SendOutcome.TransientFailure($"Push endpoint returned {(int)ex.StatusCode}.");
        }
        catch (HttpRequestException ex)
        {
            return new SendOutcome.TransientFailure(ex.Message);
        }
    }
}
