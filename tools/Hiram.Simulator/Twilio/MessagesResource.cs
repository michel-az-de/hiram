using System.Text.Json;
using System.Text.Json.Serialization;

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
            new(201, Serialize(new Message(sid, "queued", null))),

        // Accepted, then reported terminal on the message itself. Twilio does this, and the adapter has a
        // branch for it, so the double has to be able to produce it.
        ProviderScenario.CarrierFiltered =>
            new(201, Serialize(new Message(sid, "failed", "Message filtered by the carrier."))),

        ProviderScenario.GeoPermissionDenied =>
            new(400, Serialize(new Error(21408, "Permission to send an SMS has not been enabled for the region indicated by the To number."))),

        ProviderScenario.RecipientOptedOut =>
            new(400, Serialize(new Error(21610, "Attempt to send to unsubscribed recipient."))),

        ProviderScenario.OutsideSessionWindow =>
            new(400, Serialize(new Error(63016, "Failed to send freeform message because you are outside the allowed window."))),

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
        [property: JsonPropertyName("error_message")] string? ErrorMessage);

    private sealed record Error(
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("message")] string Message);
}
