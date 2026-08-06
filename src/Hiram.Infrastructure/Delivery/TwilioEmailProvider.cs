using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hiram.Application.Delivery;

namespace Hiram.Infrastructure.Delivery;

public sealed class TwilioEmailProvider : IEmailProvider
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    public TwilioEmailProvider(HttpClient http)
    {
        _http = http;
    }

    public string Name => "twilio-email";

    public async Task<SendOutcome> SendAsync(EmailMessage message, EmailProviderSettings settings, CancellationToken cancellationToken)
    {
        var from = settings.Values.GetValueOrDefault("from");
        var apiKeySid = settings.Values.GetValueOrDefault("api_key_sid");
        var apiKeySecret = settings.Secret;
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(apiKeySid) || string.IsNullOrWhiteSpace(apiKeySecret))
            return new SendOutcome.PermanentFailure("Twilio email requires a from address, an api key sid and an api key secret.");

        var content = ContentFor(message, settings);
        if (content is null)
            return new SendOutcome.PermanentFailure(
                "Trial mode requires trial_subject and trial_html in the provider settings.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "Emails")
        {
            Content = JsonContent.Create(
                new TwilioEmailRequest(
                    new TwilioAddress(from, settings.Values.GetValueOrDefault("from_name")),
                    [new TwilioAddress(message.Recipient, null)],
                    content),
                options: Json)
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
            return new SendOutcome.TransientFailure($"Twilio email request failed: {ex.Message}");
        }
    }

    // While the account is on trial the API accepts only its own approved messages, so the tenant configures
    // which one goes out. The notification body stays persisted either way (ADR-028).
    private static TwilioContent? ContentFor(EmailMessage message, EmailProviderSettings settings)
    {
        if (!string.Equals(settings.Values.GetValueOrDefault("trial_mode"), "true", StringComparison.OrdinalIgnoreCase))
            return new TwilioContent(message.Subject, message.Body);

        var subject = settings.Values.GetValueOrDefault("trial_subject");
        var html = settings.Values.GetValueOrDefault("trial_html");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(html))
            return null;

        return new TwilioContent(subject, html);
    }

    private static async Task<SendOutcome> ClassifyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var code = (int)response.StatusCode;
        if (code is >= 200 and < 300)
            return new SendOutcome.Sent(await ReadOperationIdAsync(response, cancellationToken));

        if (response.StatusCode == HttpStatusCode.TooManyRequests || code >= 500)
            return new SendOutcome.TransientFailure($"Twilio email returned {code}.");

        // A rejected send is usually the trial refusing content that is not one of its approved messages.
        // Carrying the provider's own message makes the dead letter say which, instead of just the status.
        var detail = await ReadErrorMessageAsync(response, cancellationToken);
        return detail is null
            ? new SendOutcome.PermanentFailure($"Twilio email rejected the request with {code}.")
            : new SendOutcome.PermanentFailure($"Twilio email rejected the request with {code}: {detail}");
    }

    // The accepted send returns an operation id, the handle a status callback correlates on. A 2xx without a
    // parseable id is still a send, so a missing id degrades to null rather than failing the attempt.
    private static async Task<string?> ReadOperationIdAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            var parsed = JsonSerializer.Deserialize<TwilioAcceptedResponse>(body);
            return string.IsNullOrWhiteSpace(parsed?.OperationId) ? null : parsed.OperationId;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string?> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            var parsed = JsonSerializer.Deserialize<TwilioErrorResponse>(body);
            var message = parsed?.Message ?? parsed?.Errors?.FirstOrDefault()?.Message;
            return string.IsNullOrWhiteSpace(message) ? null : message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record TwilioEmailRequest(
        [property: JsonPropertyName("from")] TwilioAddress From,
        [property: JsonPropertyName("to")] TwilioAddress[] To,
        [property: JsonPropertyName("content")] TwilioContent Content);

    private sealed record TwilioAddress(
        [property: JsonPropertyName("address")] string Address,
        [property: JsonPropertyName("name")] string? Name);

    private sealed record TwilioContent(
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html);

    private sealed record TwilioAcceptedResponse(
        [property: JsonPropertyName("operationId")] string? OperationId);

    private sealed record TwilioErrorResponse(
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("errors")] TwilioError[]? Errors);

    private sealed record TwilioError(
        [property: JsonPropertyName("message")] string? Message);
}
