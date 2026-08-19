namespace Hiram.Simulator.Twilio;

// What the double answers next. The double exists to provoke the bad path, which is the part a stubbed
// HttpMessageHandler in CI covers worst, so every value here maps to a failure a real account produces.
public enum ProviderScenario
{
    Accept,
    GeoPermissionDenied,
    RecipientOptedOut,
    CarrierFiltered,
    UnreachableHandset,
    UnknownHandset,
    OutsideSessionWindow,
    RateLimited,
    ServerError
}

public static class ProviderScenarios
{
    // Accepts either the name or the provider's own error code, because the code is what shows up in a
    // dead letter and is therefore what someone reproducing an incident has in hand.
    public static bool TryParse(string? value, out ProviderScenario scenario)
    {
        scenario = ProviderScenario.Accept;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        switch (value.Trim().ToLowerInvariant())
        {
            case "accept":
            case "ok":
                scenario = ProviderScenario.Accept;
                return true;
            case "21408":
            case "geo":
                scenario = ProviderScenario.GeoPermissionDenied;
                return true;
            case "21610":
            case "optout":
                scenario = ProviderScenario.RecipientOptedOut;
                return true;
            case "30007":
            case "filtered":
                scenario = ProviderScenario.CarrierFiltered;
                return true;
            case "30003":
            case "unreachable":
                scenario = ProviderScenario.UnreachableHandset;
                return true;
            case "30005":
            case "unknown":
                scenario = ProviderScenario.UnknownHandset;
                return true;
            case "63016":
            case "window":
                scenario = ProviderScenario.OutsideSessionWindow;
                return true;
            case "429":
            case "ratelimited":
                scenario = ProviderScenario.RateLimited;
                return true;
            case "500":
            case "servererror":
                scenario = ProviderScenario.ServerError;
                return true;
            default:
                return false;
        }
    }

    public static string Describe(ProviderScenario scenario) => scenario switch
    {
        ProviderScenario.Accept => "accept (201 queued)",
        ProviderScenario.GeoPermissionDenied => "21408, region not enabled in geo permissions",
        ProviderScenario.RecipientOptedOut => "21610, recipient replied STOP",
        ProviderScenario.CarrierFiltered => "30007, accepted then filtered as spam by the carrier",
        ProviderScenario.UnreachableHandset => "30003, handset unreachable, worth another attempt",
        ProviderScenario.UnknownHandset => "30005, the number does not exist",
        ProviderScenario.OutsideSessionWindow => "63016, free form outside the 24h WhatsApp window",
        ProviderScenario.RateLimited => "429, rate limited",
        ProviderScenario.ServerError => "500, provider side error",
        _ => scenario.ToString()
    };
}
