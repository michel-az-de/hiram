using System.Net.Http.Headers;
using System.Text;
using Hiram.Simulator.Providers;

namespace Hiram.Simulator.Twilio;

// A local stand in for the Twilio API, spoken over real HTTP. A stubbed handler proves an adapter; this
// proves the composition around it, which is where the defect that opened issue #139 lived: serialization,
// authorization header, form encoding, timeout and the resilience pipeline all run for real here.
public sealed class TwilioDouble : IProviderDouble
{
    private readonly SidSequence _messageSids = new("SM");
    private readonly SidSequence _emailOperations = new("EM");
    private readonly List<string> _log = [];
    private readonly Lock _gate = new();

    private ProviderScenario _scenario;

    public TwilioDouble(ProviderScenario scenario)
    {
        _scenario = scenario;
    }

    public ProviderScenario Scenario
    {
        get { lock (_gate) return _scenario; }
        set { lock (_gate) _scenario = value; }
    }

    public IReadOnlyList<string> Log
    {
        get { lock (_gate) return _log.ToArray(); }
    }

    public int MessagesAccepted => _messageSids.Issued;

    public int EmailsAccepted => _emailOperations.Issued;

    public string Name => "twilio";

    public string WalkthroughChannel => "sms";

    public IReadOnlyList<ProviderConfig> Configs =>
    [
        new("sms", "twilio-sms", MessagingSettings),
        new("whatsapp", "twilio-whatsapp", MessagingSettings)
    ];

    private static IReadOnlyDictionary<string, string> MessagingSettings => new Dictionary<string, string>
    {
        ["account_sid"] = "AC00000000000000000000000000000000",
        ["api_key_sid"] = "SK00000000000000000000000000000000",
        ["from"] = "+15005550006"
    };

    // Twilio answers every scenario the enum has except the three the Cloud API introduced, which have no
    // counterpart on this side.
    public bool Supports(ProviderScenario scenario) => scenario
        is not (ProviderScenario.TemplateParametersMismatch
            or ProviderScenario.TokenExpired
            or ProviderScenario.AccountRestricted);

    public void MapInto(IEndpointRouteBuilder endpoints)
    {
        // The real paths, so the only thing an environment has to change is the host.
        endpoints.MapPost("/2010-04-01/Accounts/{accountSid}/Messages.json", HandleMessageAsync);
        endpoints.MapPost("/v1/Emails", HandleEmailAsync);
    }

    public IReadOnlyList<string> Wiring(Uri address) =>
    [
        $"Hiram__Providers__Endpoints__TwilioApi={address}",
        $"Hiram__Providers__Endpoints__TwilioEmail={new Uri(address, "v1/")}"
    ];

    private async Task<IResult> HandleMessageAsync(HttpRequest request, string accountSid)
    {
        if (!HasCredential(request))
            return Write(MessagesResource.Unauthorized());

        var form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
        var channel = (form["To"].ToString() ?? string.Empty).StartsWith("whatsapp:", StringComparison.Ordinal)
            ? "whatsapp"
            : "sms";

        var scenario = Scenario;
        var response = MessagesResource.For(scenario, _messageSids.Next());
        Record($"{channel} to {Mask(form["To"].ToString())} on {accountSid}, answered {response.StatusCode} ({ProviderScenarios.Describe(scenario)})");

        return Write(response);
    }

    private async Task<IResult> HandleEmailAsync(HttpRequest request)
    {
        if (!HasCredential(request))
            return Write(EmailsResource.Unauthorized());

        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);

        var scenario = Scenario;
        var response = EmailsResource.For(scenario, _emailOperations.Next());
        Record($"email of {body.Length} bytes, answered {response.StatusCode} ({ProviderScenarios.Describe(scenario)})");

        return Write(response);
    }

    // The double never checks the secret, and does require one to be present and parseable: an adapter
    // that forgets to authenticate has to fail here the same way it would fail against Twilio.
    private static bool HasCredential(HttpRequest request)
    {
        if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization.ToString(), out var header))
            return false;

        if (!string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) || header.Parameter is null)
            return false;

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter)).Contains(':', StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static IResult Write(ProviderResponse response) =>
        Results.Content(response.Body, ProviderResponse.ContentType, Encoding.UTF8, response.StatusCode);

    // The log is read from the console thread while requests are being served, so it is copied under the
    // same lock that appends to it.
    private void Record(string line)
    {
        lock (_gate)
            _log.Add(line);
    }

    // A destination is personal data even in a local run, and the log is meant to be pasted into an issue.
    private static string Mask(string destination) =>
        destination.Length <= 6 ? destination : string.Concat(destination.AsSpan(0, 6), "…", destination.AsSpan(destination.Length - 2));

}
