using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hiram.Application.Delivery;

namespace Hiram.Infrastructure.Delivery;

// Meta's Cloud API, the direct path with no BSP in front of it (ADR-030). One POST of JSON, which is why
// this adapter carries no dependency: the value here is the error classification, not the transport.
public sealed class MetaWhatsAppProvider : IWhatsAppProvider
{
    private const string Channel = "Meta WhatsApp";

    private readonly HttpClient _http;
    private readonly string _defaultGraphVersion;

    public MetaWhatsAppProvider(HttpClient http, string defaultGraphVersion)
    {
        _http = http;
        _defaultGraphVersion = defaultGraphVersion;
    }

    public string Name => ProviderNames.MetaWhatsApp;

    public async Task<SendOutcome> SendAsync(WhatsAppMessage message, WhatsAppProviderSettings settings, CancellationToken cancellationToken)
    {
        var phoneNumberId = settings.Values.GetValueOrDefault("phone_number_id");
        var accessToken = settings.Secret;
        if (string.IsNullOrWhiteSpace(phoneNumberId) || string.IsNullOrWhiteSpace(accessToken))
            return new SendOutcome.PermanentFailure(
                "Meta WhatsApp requires phone_number_id and an access token.",
                DeliveryFailureKind.Configuration);

        // A tenant can pin its own version while it migrates, which is the whole reason the version is not
        // a compiled constant. Absent, the host default applies.
        var graphVersion = settings.Values.GetValueOrDefault("graph_version") is { Length: > 0 } pinned
            ? pinned
            : _defaultGraphVersion;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{graphVersion}/{phoneNumberId}/messages")
        {
            Content = JsonContent.Create(Payload(message), options: SerializerOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

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
            return new SendOutcome.TransientFailure($"{Channel} request failed: {ex.Message}");
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions =
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private static SendRequest Payload(WhatsAppMessage message) => message switch
    {
        WhatsAppMessage.FreeForm freeForm => new SendRequest(
            freeForm.Recipient, "text", Text: new TextBody(freeForm.Body)),

        // Meta renders the template, so what goes out is the name, the language and the values in order.
        // An empty parameter list still sends a components array with an empty body, which Meta reads as a
        // template that takes none, rather than as a template whose values were forgotten.
        WhatsAppMessage.Template template => new SendRequest(
            template.Recipient, "template", Template: new TemplateBody(
                template.Name,
                new TemplateLanguage(template.Language),
                [new TemplateComponent("body", [.. template.Parameters.Select(value => new TemplateParameter("text", value))])])),

        _ => throw new InvalidOperationException($"No Cloud API payload is defined for '{message.GetType().Name}'.")
    };

    private static async Task<SendOutcome> ClassifyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var code = (int)response.StatusCode;

        if (code is >= 200 and < 300)
        {
            // messages[0].id is the wamid, the handle a status callback matches on (ADR-030, edge 6).
            var accepted = Parse<AcceptedResponse>(body);
            return new SendOutcome.Sent(accepted?.Messages?.FirstOrDefault()?.Id);
        }

        // Carrying Meta's own message makes the dead letter name the cause, such as a template missing in
        // that language, instead of only the status code.
        var error = Parse<ErrorResponse>(body)?.Error;
        var rejected = Describe(code, error);

        // The policy reads the code, which is the only thing that says what happened. The status range is
        // the fallback for a code nobody mapped, and for a body that did not parse at all.
        if (error?.Code is { } known && MetaErrorPolicy.For(known, rejected) is { } decided)
            return decided;

        if (response.StatusCode == HttpStatusCode.TooManyRequests || code >= 500)
            return new SendOutcome.TransientFailure($"{Channel} returned {code}.");

        return new SendOutcome.PermanentFailure(rejected);
    }

    private static string Describe(int status, MetaError? error)
    {
        var fallback = $"{Channel} rejected the request with {status}.";
        if (error is null)
            return fallback;

        // error_data.details is where Meta puts the sentence a human can act on; message alone is a title.
        var detail = error.Data?.Details is { Length: > 0 } details ? details : error.Message;
        if (string.IsNullOrWhiteSpace(detail))
            return fallback;

        return error.Code is { } code ? $"{fallback} {code}: {detail}" : $"{fallback} {detail}";
    }

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

    private sealed record SendRequest(
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] TextBody? Text = null,
        [property: JsonPropertyName("template")] TemplateBody? Template = null)
    {
        [JsonPropertyName("messaging_product")]
        public string MessagingProduct => "whatsapp";

        [JsonPropertyName("recipient_type")]
        public string RecipientType => "individual";
    }

    private sealed record TextBody([property: JsonPropertyName("body")] string Body);

    private sealed record TemplateBody(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("language")] TemplateLanguage Language,
        [property: JsonPropertyName("components")] IReadOnlyList<TemplateComponent> Components);

    private sealed record TemplateLanguage([property: JsonPropertyName("code")] string Code);

    private sealed record TemplateComponent(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("parameters")] IReadOnlyList<TemplateParameter> Parameters);

    private sealed record TemplateParameter(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text);

    private sealed record AcceptedResponse(
        [property: JsonPropertyName("messages")] IReadOnlyList<AcceptedMessage>? Messages);

    private sealed record AcceptedMessage([property: JsonPropertyName("id")] string? Id);

    private sealed record ErrorResponse([property: JsonPropertyName("error")] MetaError? Error);

    private sealed record MetaError(
        [property: JsonPropertyName("code")] int? Code,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("error_data")] MetaErrorData? Data);

    private sealed record MetaErrorData([property: JsonPropertyName("details")] string? Details);
}
