using System.Net.Http.Headers;
using System.Text;
using Hiram.Application.Delivery;

namespace Hiram.Infrastructure.Delivery;

public sealed class TwilioSmsProvider : ISmsProvider
{
    private const string Channel = "Twilio SMS";

    private readonly HttpClient _http;

    public TwilioSmsProvider(HttpClient http)
    {
        _http = http;
    }

    public string Name => "twilio-sms";

    public async Task<SendOutcome> SendAsync(SmsMessage message, SmsProviderSettings settings, CancellationToken cancellationToken)
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
                "Twilio SMS requires account_sid, from, api_key_sid and an api key secret.");

        var trial = IsTrial(settings);
        var body = BodyFor(message, settings, trial);
        if (body is null)
            return new SendOutcome.PermanentFailure("Trial mode requires trial_template in the provider settings.");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"2010-04-01/Accounts/{accountSid}/Messages.json")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = message.Recipient,
                ["From"] = from,
                ["Body"] = body
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKeySid}:{apiKeySecret}")));

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            var outcome = await TwilioMessagesApi.ClassifyAsync(Channel, response, cancellationToken);

            // What went out was the approved text, not the persisted body. The attempt carries that so the
            // delivery history does not read as if the notification itself had been sent.
            return trial && outcome is SendOutcome.Sent sent ? sent with { TrialContent = true } : outcome;
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

    private static bool IsTrial(SmsProviderSettings settings) =>
        string.Equals(settings.Values.GetValueOrDefault("trial_mode"), "true", StringComparison.OrdinalIgnoreCase);

    // A trial account rejects free text and accepts only the key of one of its canned messages, which it
    // expands into the final text on its side. The notification body stays persisted either way (ADR-028).
    private static string? BodyFor(SmsMessage message, SmsProviderSettings settings, bool trial)
    {
        if (!trial)
            return message.Body;

        var template = settings.Values.GetValueOrDefault("trial_template");
        return string.IsNullOrWhiteSpace(template) ? null : template;
    }
}
