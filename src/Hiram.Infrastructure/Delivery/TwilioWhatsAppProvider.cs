using System.Net.Http.Headers;
using System.Text;
using Hiram.Application.Delivery;

namespace Hiram.Infrastructure.Delivery;

public sealed class TwilioWhatsAppProvider : IWhatsAppProvider
{
    private const string Channel = "Twilio WhatsApp";

    // The Messages resource tells WhatsApp from SMS by the address prefix alone. It lives here and
    // nowhere else, so notification_requests, the fan-out and the E.164 rule all keep the bare number.
    private const string AddressPrefix = "whatsapp:";

    private readonly HttpClient _http;

    public TwilioWhatsAppProvider(HttpClient http)
    {
        _http = http;
    }

    public string Name => ProviderNames.TwilioWhatsApp;

    public async Task<SendOutcome> SendAsync(WhatsAppMessage message, WhatsAppProviderSettings settings, CancellationToken cancellationToken)
    {
        var accountSid = settings.Values.GetValueOrDefault("account_sid");
        var from = settings.Values.GetValueOrDefault("from");
        var apiKeySid = settings.Values.GetValueOrDefault("api_key_sid");
        var apiKeySecret = settings.Secret;
        if (string.IsNullOrWhiteSpace(accountSid)
            || string.IsNullOrWhiteSpace(from)
            || string.IsNullOrWhiteSpace(apiKeySid)
            || string.IsNullOrWhiteSpace(apiKeySecret))
            return new SendOutcome.PermanentFailure(
                "Twilio WhatsApp requires account_sid, from, api_key_sid and an api key secret.");

        // The notification body goes out as written. Unlike SMS and email, the sandbox accepts free text
        // inside the 24h session the recipient opens by joining; outside it Twilio answers 63016, which
        // classifies as permanent and names itself in the dead letter, so a replay after a rejoin works.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"2010-04-01/Accounts/{accountSid}/Messages.json")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = AddressPrefix + message.Recipient,
                ["From"] = AddressPrefix + from,
                ["Body"] = message.Body
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKeySid}:{apiKeySecret}")));

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            return await TwilioMessagesApi.ClassifyAsync(Channel, response, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            // Transport failures and timeouts are worth another attempt.
            return new SendOutcome.TransientFailure($"{Channel} request failed: {ex.Message}");
        }
    }
}
