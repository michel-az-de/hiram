using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

    public string Name => "resend";

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
            return Classify(response.StatusCode);
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

    private static SendOutcome Classify(HttpStatusCode status)
    {
        var code = (int)status;
        if (code is >= 200 and < 300)
            return new SendOutcome.Sent();

        if (status == HttpStatusCode.TooManyRequests || code >= 500)
            return new SendOutcome.TransientFailure($"Resend returned {code}.");

        return new SendOutcome.PermanentFailure($"Resend rejected the request with {code}.");
    }

    private sealed record ResendSendRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("text")] string Text);
}
