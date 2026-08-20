namespace Hiram.Simulator.Providers;

// What the double answers next. The double exists to provoke the bad path, which is the part a stubbed
// HttpMessageHandler in CI covers worst, so every value here maps to a failure a real account produces.
public enum ProviderScenario
{
    Accept,
    GeoPermissionDenied,
    CampaignNotRegistered,
    RecipientOptedOut,
    CarrierFiltered,
    UnreachableHandset,
    UnknownHandset,
    OutsideSessionWindow,
    TemplateRequired,
    RateLimited,
    ServerError,

    // Introduced by the Cloud API, which has no counterpart on the Twilio side.
    TemplateParametersMismatch,
    TokenExpired,
    AccountRestricted
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
            case "30034":
            case "10dlc":
                scenario = ProviderScenario.CampaignNotRegistered;
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
            case "131026":
            case "unknown":
                scenario = ProviderScenario.UnknownHandset;
                return true;
            case "63016":
            case "131047":
            case "window":
                scenario = ProviderScenario.OutsideSessionWindow;
                return true;
            case "21654":
            case "132001":
            case "template":
                scenario = ProviderScenario.TemplateRequired;
                return true;
            case "429":
            case "130429":
            case "ratelimited":
                scenario = ProviderScenario.RateLimited;
                return true;
            case "500":
            case "131000":
            case "servererror":
                scenario = ProviderScenario.ServerError;
                return true;
            case "132000":
            case "parameters":
                scenario = ProviderScenario.TemplateParametersMismatch;
                return true;
            case "190":
            case "token":
                scenario = ProviderScenario.TokenExpired;
                return true;
            case "131031":
            case "restricted":
                scenario = ProviderScenario.AccountRestricted;
                return true;
            default:
                return false;
        }
    }

    // What --scenario accepts, derived from the enum instead of written down a second time: the hand
    // kept list went stale the moment a scenario was added, and the help then advertised half of what works.
    public static string Codes => string.Join(" | ", Enum.GetValues<ProviderScenario>().Select(Alias));

    // The provider neutral name of a scenario. Codes are the handle someone reproducing an incident has,
    // and they differ per provider for the same failure: a closed window is 63016 on Twilio and 131047 on
    // Meta. Listing what a double accepts has to use the word both understand.
    public static string Alias(ProviderScenario scenario) => scenario switch
    {
        ProviderScenario.Accept => "accept",
        ProviderScenario.GeoPermissionDenied => "geo",
        ProviderScenario.CampaignNotRegistered => "10dlc",
        ProviderScenario.RecipientOptedOut => "optout",
        ProviderScenario.CarrierFiltered => "filtered",
        ProviderScenario.UnreachableHandset => "unreachable",
        ProviderScenario.UnknownHandset => "unknown",
        ProviderScenario.OutsideSessionWindow => "window",
        ProviderScenario.TemplateRequired => "template",
        ProviderScenario.RateLimited => "ratelimited",
        ProviderScenario.ServerError => "servererror",
        ProviderScenario.TemplateParametersMismatch => "parameters",
        ProviderScenario.TokenExpired => "token",
        ProviderScenario.AccountRestricted => "restricted",
        _ => scenario.ToString().ToLowerInvariant()
    };

    public static string Describe(ProviderScenario scenario) => scenario switch
    {
        ProviderScenario.Accept => "accept (201 queued)",
        ProviderScenario.GeoPermissionDenied => "21408, region not enabled in geo permissions",
        ProviderScenario.CampaignNotRegistered => "30034, the US sender is not registered in a 10DLC campaign",
        ProviderScenario.RecipientOptedOut => "21610, recipient replied STOP",
        ProviderScenario.CarrierFiltered => "30007, accepted then filtered as spam by the carrier",
        ProviderScenario.UnreachableHandset => "30003, handset unreachable, worth another attempt",
        ProviderScenario.UnknownHandset => "30005, the number does not exist",
        ProviderScenario.OutsideSessionWindow => "63016, free form outside the 24h WhatsApp window, as documented",
        ProviderScenario.TemplateRequired => "21654, ContentSid required, what a closed window actually answers",
        ProviderScenario.RateLimited => "429, rate limited",
        ProviderScenario.ServerError => "500, provider side error",
        ProviderScenario.TemplateParametersMismatch => "132000, the template takes a different number of values",
        ProviderScenario.TokenExpired => "190, the access token expired",
        ProviderScenario.AccountRestricted => "131031, the business account is restricted for a policy violation",
        _ => scenario.ToString()
    };
}
