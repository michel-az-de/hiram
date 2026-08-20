using System.Text.Json;
using System.Text.Json.Serialization;
using Hiram.Simulator.Providers;

namespace Hiram.Simulator.Twilio;

// The shapes the Messages resource answers with. They exist here in one place so the double cannot drift
// from what TwilioMessagesApi already parses: the parity test feeds these exact bodies to the real
// adapters and asserts the classification, instead of a second copy of the rule living in the double.
public static class MessagesResource
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static ProviderResponse For(ProviderScenario scenario, string sid) => scenario switch
    {
        ProviderScenario.Accept =>
            new(201, Serialize(new Message(sid, "queued", null, null))),

        // Accepted, then reported terminal on the message itself, with the code that decides what the
        // failure means. Carrier verdicts arrive this way and never as a rejected request.
        ProviderScenario.CarrierFiltered =>
            new(201, Serialize(new Message(sid, "undelivered", 30007, "Message filtered by the carrier."))),

        ProviderScenario.UnreachableHandset =>
            new(201, Serialize(new Message(sid, "undelivered", 30003, "Unreachable destination handset."))),

        ProviderScenario.UnknownHandset =>
            new(201, Serialize(new Message(sid, "undelivered", 30005, "Unknown destination handset."))),

        ProviderScenario.GeoPermissionDenied =>
            new(400, Serialize(new Error(21408, "Permission to send an SMS has not been enabled for the region indicated by the To number."))),

        // The US sender is not registered in a 10DLC campaign, which is an account level step and says
        // nothing about this recipient. Failing early beats paying for what no carrier will take.
        ProviderScenario.CampaignNotRegistered =>
            new(400, Serialize(new Error(30034, "Message From an Unregistered Number."))),

        ProviderScenario.RecipientOptedOut =>
            new(400, Serialize(new Error(21610, "Attempt to send to unsubscribed recipient."))),

        ProviderScenario.OutsideSessionWindow =>
            new(400, Serialize(new Error(63016, "Failed to send freeform message because you are outside the allowed window."))),

        // What a closed WhatsApp session was actually measured answering, six times out of six, where the
        // documentation predicts 63016 (issue #133). The double can produce both because only one of them
        // has ever been observed and the other is still in the provider's error table.
        ProviderScenario.TemplateRequired =>
            new(400, Serialize(new Error(21654, "ContentSid Required."))),

        ProviderScenario.RateLimited =>
            new(429, Serialize(new Error(20429, "Too Many Requests."))),

        ProviderScenario.ServerError =>
            new(500, Serialize(new Error(20500, "Internal Server Error."))),

        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown provider scenario.")
    };

    public static ProviderResponse Unauthorized() =>
        new(401, Serialize(new Error(20003, "Authentication Error, no credentials provided.")));

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    private sealed record Message(
        [property: JsonPropertyName("sid")] string Sid,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("error_code")] int? ErrorCode,
        [property: JsonPropertyName("error_message")] string? ErrorMessage);

    private sealed record Error(
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("message")] string Message);
}
