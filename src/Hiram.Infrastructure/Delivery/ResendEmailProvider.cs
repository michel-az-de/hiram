using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hiram.Application.Delivery;

namespace Hiram.Infrastructure.Delivery;

public sealed class ResendEmailProvider : IEmailProvider
{
    private readonly HttpClient _http;

    public ResendEmailProvider(HttpClient http)
    {
        _http = http;
    }

    public string Name => ProviderNames.Resend;

    public async Task<SendOutcome> SendAsync(EmailMessage message, EmailProviderSettings settings, CancellationToken cancellationToken)
    {
        var from = settings.Values.GetValueOrDefault("from");
        var apiKey = settings.Secret;
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(apiKey))
            return new SendOutcome.PermanentFailure("Resend provider requires a from address and an API key.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new ResendSendRequest(from, [message.Recipient], message.Subject, message.Body))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

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
            return new SendOutcome.TransientFailure($"Resend request failed: {ex.Message}");
        }
    }

    private static async Task<SendOutcome> ClassifyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var code = (int)response.StatusCode;
        if (code is >= 200 and < 300)
            return new SendOutcome.Sent(await ReadMessageIdAsync(response, cancellationToken));

        if (response.StatusCode == HttpStatusCode.TooManyRequests || code >= 500)
            return new SendOutcome.TransientFailure($"Resend returned {code}.");

        return new SendOutcome.PermanentFailure($"Resend rejected the request with {code}.");
    }

    // Resend returns the accepted message id in the body, the handle a delivery callback correlates on. A 2xx
    // without a parseable id is still a send, so a missing id degrades to null rather than failing the attempt.
    private static async Task<string?> ReadMessageIdAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            var parsed = JsonSerializer.Deserialize<ResendSendResponse>(body);
            return string.IsNullOrWhiteSpace(parsed?.Id) ? null : parsed.Id;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ResendSendRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("text")] string Text);

    private sealed record ResendSendResponse(
        [property: JsonPropertyName("id")] string? Id);
}
