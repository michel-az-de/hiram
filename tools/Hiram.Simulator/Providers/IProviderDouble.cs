namespace Hiram.Simulator.Providers;

// What the tenant needs configured for one channel so the double can answer for it.
public sealed record ProviderConfig(string Channel, string Provider, IReadOnlyDictionary<string, string> Settings);

// What the walkthrough needs from a provider stand in, whichever provider it is. It exists because there
// are two of them now, Twilio and Meta, and not before: the first implementation of anything is a class,
// the second is what turns it into a boundary.
public interface IProviderDouble
{
    // How the double names itself in the transcript and in error messages.
    string Name { get; }

    // The channel the three acts run on. Twilio keeps SMS, which is what it has always exercised. Meta has
    // no SMS at all, so its runs go over WhatsApp.
    string WalkthroughChannel { get; }

    // Every channel this double can answer for, with the provider value a tenant writes in
    // PUT /v1/providers/{channel} and settings filled with values the double accepts.
    IReadOnlyList<ProviderConfig> Configs { get; }

    ProviderScenario Scenario { get; set; }

    IReadOnlyList<string> Log { get; }

    // False when this provider has no such failure. A double that answers an error the real API never
    // returns is worse than no double, because the run would prove a classification that cannot happen.
    bool Supports(ProviderScenario scenario);

    void MapInto(IEndpointRouteBuilder endpoints);

    // What to print so someone can point a Hiram at this double.
    IReadOnlyList<string> Wiring(Uri address);
}
