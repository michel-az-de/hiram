namespace Hiram.Application.Delivery;

// The host each adapter talks to, as configuration rather than a compiled constant, so a local double can
// stand in for the real API without any change to the delivery path. Production is the default: an
// environment that configures nothing keeps talking to the real providers.
//
// MetaGraphVersion is the odd one out, a version rather than a host, and it belongs here for the same
// reason the hosts do: it is part of the address. Meta puts the version in the path, force-upgrades
// integrations pinned to a version it stops serving, and on 2026-08-20 three sources disagreed on which
// one to use, the get-started docs at v23.0, the Graph API changelog at v26.0 and the most used .NET
// wrapper at v25.0. A compiled constant would make that a deploy; a tenant can still override it.
public sealed record ProviderEndpoints(Uri Resend, Uri TwilioEmail, Uri TwilioApi, Uri MetaGraph, string MetaGraphVersion)
{
    public static ProviderEndpoints Production { get; } = new(
        new Uri("https://api.resend.com/"),
        new Uri("https://comms.twilio.com/v1/"),
        new Uri("https://api.twilio.com/"),
        new Uri("https://graph.facebook.com/"),
        "v23.0");
}
