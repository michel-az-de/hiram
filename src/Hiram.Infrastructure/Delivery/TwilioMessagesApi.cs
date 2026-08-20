using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hiram.Application.Delivery;

namespace Hiram.Infrastructure.Delivery;

// The Messages resource answers SMS and WhatsApp with the same two payload shapes, so both adapters read
// a response through here. One reader is what keeps a rejection such as 21608 or 63016 from being
// classified one way on one channel and another way on the other.
internal static class TwilioMessagesApi
{
    public static async Task<SendOutcome> ClassifyAsync(
        string channel, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var code = (int)response.StatusCode;

        if (code is >= 200 and < 300)
        {
            var message = Parse<MessageResource>(body);

            // Twilio can accept the request and still report a terminal status on the message itself. This
            // is how the carrier verdicts arrive, so the code has to be read here and not only on a 4xx.
            if (message?.Status is "failed" or "undelivered")
            {
                var reported = Describe($"{channel} reported {message.Status}.", message.ErrorCode, message.ErrorMessage);
                return Decide(message.ErrorCode, reported)
                    ?? new SendOutcome.PermanentFailure(reported);
            }

            return new SendOutcome.Sent(message?.Sid);
        }

        // Carrying the provider's own message makes the dead letter name the cause, such as an unverified
        // recipient or free text refused outside the session window, instead of only the status code.
        var error = Parse<ErrorResponse>(body);
        var rejected = Describe($"{channel} rejected the request with {code}.", error?.Code, error?.Message);

        // The policy reads the code, which is the only thing that says what happened. The status range is
        // the fallback for a code nobody mapped, and for a body that did not parse at all.
        var decided = Decide(error?.Code, rejected);
        if (decided is not null)
            return decided;

        if (response.StatusCode == HttpStatusCode.TooManyRequests || code >= 500)
            return new SendOutcome.TransientFailure($"{channel} returned {code}.");

        return new SendOutcome.PermanentFailure(rejected);
    }

    private static SendOutcome? Decide(int? errorCode, string describe) =>
        errorCode is { } code ? TwilioErrorPolicy.For(code, describe) : null;

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

    private sealed record MessageResource(
        [property: JsonPropertyName("sid")] string? Sid,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("error_code")] int? ErrorCode,
        [property: JsonPropertyName("error_message")] string? ErrorMessage);

    private sealed record ErrorResponse(
        [property: JsonPropertyName("code")] int? Code,
        [property: JsonPropertyName("message")] string? Message);
}
