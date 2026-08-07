using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hiram.Application.Delivery;

namespace Hiram.Infrastructure.Delivery;

public sealed class TwilioSmsProvider : ISmsProvider
{
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

        var body = BodyFor(message, settings);
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
            return await ClassifyAsync(response, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            // Transport failures and timeouts are worth another attempt.
            return new SendOutcome.TransientFailure($"Twilio SMS request failed: {ex.Message}");
        }
    }

    // A trial account rejects free text and accepts only the key of one of its canned messages, which it
    // expands into the final text on its side. The notification body stays persisted either way (ADR-028).
    private static string? BodyFor(SmsMessage message, SmsProviderSettings settings)
    {
        if (!string.Equals(settings.Values.GetValueOrDefault("trial_mode"), "true", StringComparison.OrdinalIgnoreCase))
            return message.Body;

        var template = settings.Values.GetValueOrDefault("trial_template");
        return string.IsNullOrWhiteSpace(template) ? null : template;
    }

    private static async Task<SendOutcome> ClassifyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var code = (int)response.StatusCode;

        if (code is >= 200 and < 300)
        {
            var message = Parse<TwilioMessageResponse>(body);

            // Twilio can accept the request and still report a terminal status on the message itself.
            if (message?.Status is "failed" or "undelivered")
                return new SendOutcome.PermanentFailure(
                    Describe($"Twilio SMS reported {message.Status}.", null, message.ErrorMessage));

            return new SendOutcome.Sent(message?.Sid);
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests || code >= 500)
            return new SendOutcome.TransientFailure($"Twilio SMS returned {code}.");

        // Carrying the provider's own message makes the dead letter name the cause, such as an unverified
        // recipient or free text refused by a trial account, instead of only the status code.
        var error = Parse<TwilioErrorResponse>(body);
        return new SendOutcome.PermanentFailure(
            Describe($"Twilio SMS rejected the request with {code}.", error?.Code, error?.Message));
    }

    private static string Describe(string fallback, int? errorCode, string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return fallback;

        return errorCode is { } known ? $"{fallback} {known}: {detail}" : $"{fallback} {detail}";
    }

    // The error payload and the message resource disagree on the shape of "status": a string on the
    // message, a number on the error. Parsing them as separate types keeps one from breaking the other.
    private static T? Parse<T>(string body) where T : class
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record TwilioMessageResponse(
        [property: JsonPropertyName("sid")] string? Sid,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("error_message")] string? ErrorMessage);

    private sealed record TwilioErrorResponse(
        [property: JsonPropertyName("code")] int? Code,
        [property: JsonPropertyName("message")] string? Message);
}
