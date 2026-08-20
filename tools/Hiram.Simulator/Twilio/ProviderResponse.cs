namespace Hiram.Simulator.Twilio;

// A response the double is about to write. It is a value, not an IResult, so the same builder that serves
// the HTTP endpoint can be asserted against the real adapters without opening a port.
public sealed record ProviderResponse(int StatusCode, string Body)
{
    public const string ContentType = "application/json";
}
