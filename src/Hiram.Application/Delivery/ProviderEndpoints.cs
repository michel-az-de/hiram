namespace Hiram.Application.Delivery;

// The host each adapter talks to, as configuration rather than a compiled constant, so a local double can
// stand in for the real API without any change to the delivery path. Production is the default: an
// environment that configures nothing keeps talking to the real providers.
public sealed record ProviderEndpoints(Uri Resend, Uri TwilioEmail, Uri TwilioApi)
{
    public static ProviderEndpoints Production { get; } = new(
        new Uri("https://api.resend.com/"),
        new Uri("https://comms.twilio.com/v1/"),
        new Uri("https://api.twilio.com/"));
}
