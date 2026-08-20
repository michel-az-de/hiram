using System.Text.Json;
using Hiram.Simulator.Providers;

namespace Hiram.Simulator.Meta;

// The shapes the Cloud API answers with. They live here in one place so the double cannot drift from what
// MetaWhatsAppProvider already parses: the parity test feeds these exact bodies to the real adapter and
// asserts the classification, instead of a second copy of the rule living in the double.
public static class MetaMessagesResource
{
    // Null when the Cloud API has no such failure. Every Twilio-only scenario lands here, and the caller
    // refuses the run rather than answering an error Meta never returns.
    public static ProviderResponse? For(ProviderScenario scenario, string wamid) => scenario switch
    {
        ProviderScenario.Accept => new(200, Serialize(new Accepted(
            "whatsapp", [new Contact("5511999990000")], [new AcceptedMessage(wamid)]))),

        // The 24h window closed, so only a template goes out from here.
        ProviderScenario.OutsideSessionWindow => Error(
            400, 131047, "Message failed to send because more than 24 hours have passed since the customer last replied."),

        ProviderScenario.TemplateRequired => Error(
            400, 132001, "Template name does not exist in the translation."),

        ProviderScenario.TemplateParametersMismatch => Error(
            400, 132000, "Number of parameters does not match the expected number of params."),

        ProviderScenario.UnknownHandset => Error(
            400, 131026, "Receiver is incapable of receiving this message."),

        ProviderScenario.TokenExpired => Error(
            401, 190, "Error validating access token: Session has expired."),

        ProviderScenario.AccountRestricted => Error(
            403, 131031, "Business Account is restricted from messaging users in this country."),

        ProviderScenario.RateLimited => Error(
            429, 130429, "Cloud API message throughput has been reached."),

        ProviderScenario.ServerError => Error(
            500, 131000, "Message failed to send due to an unknown error."),

        _ => null
    };

    public static ProviderResponse Unauthorized() =>
        Error(401, 190, "An access token is required to request this resource.");

    private static ProviderResponse Error(int status, int code, string details) =>
        new(status, Serialize(new ErrorEnvelope(new MetaError(
            "Error", "OAuthException", code, new ErrorData("whatsapp", details), "AbCdEfGh"))));

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private sealed record Accepted(string MessagingProduct, IReadOnlyList<Contact> Contacts, IReadOnlyList<AcceptedMessage> Messages);

    private sealed record Contact(string WaId);

    private sealed record AcceptedMessage(string Id);

    private sealed record ErrorEnvelope(MetaError Error);

    private sealed record MetaError(string Message, string Type, int Code, ErrorData ErrorData, string FbtraceId);

    private sealed record ErrorData(string MessagingProduct, string Details);
}
