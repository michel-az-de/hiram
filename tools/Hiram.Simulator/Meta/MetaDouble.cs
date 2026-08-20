using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Hiram.Simulator.Providers;

namespace Hiram.Simulator.Meta;

// A local stand in for Meta's Cloud API, spoken over real HTTP. A stubbed handler proves the adapter; this
// proves the composition around it, which is where the defect that opened issue #139 lived: serialization,
// the bearer header, the versioned path, timeout and the resilience pipeline all run for real here.
public sealed class MetaDouble : IProviderDouble
{
    private readonly WamidSequence _wamids = new();
    private readonly List<string> _log = [];
    private readonly Lock _gate = new();

    private ProviderScenario _scenario;

    public MetaDouble(ProviderScenario scenario)
    {
        _scenario = scenario;
    }

    public string Name => "meta";

    // Meta has no SMS. The three acts run on WhatsApp, which is also the only channel it configures.
    public string WalkthroughChannel => "whatsapp";

    // phone_number_id is what the adapter puts in the path, and the token is the protected secret, so it
    // is not here. graph_version is left out on purpose: a run should exercise the host default.
    public IReadOnlyList<ProviderConfig> Configs =>
    [
        new("whatsapp", "meta-whatsapp", new Dictionary<string, string> { ["phone_number_id"] = "100000000000000" })
    ];

    public ProviderScenario Scenario
    {
        get { lock (_gate) return _scenario; }
        set { lock (_gate) _scenario = value; }
    }

    public IReadOnlyList<string> Log
    {
        get { lock (_gate) return _log.ToArray(); }
    }

    public int MessagesAccepted => _wamids.Issued;

    public bool Supports(ProviderScenario scenario) => MetaMessagesResource.For(scenario, "wamid.probe") is not null;

    public void MapInto(IEndpointRouteBuilder endpoints)
    {
        // The real path, version included, so the only thing an environment has to change is the host. The
        // version is a route value rather than a literal because a tenant can pin its own.
        endpoints.MapPost("/{version}/{phoneNumberId}/messages", HandleMessageAsync);
    }

    public IReadOnlyList<string> Wiring(Uri address) =>
    [
        $"Hiram__Providers__Endpoints__MetaGraph={address}"
    ];

    private async Task<IResult> HandleMessageAsync(HttpRequest request, string version, string phoneNumberId)
    {
        if (!HasCredential(request))
            return Write(MetaMessagesResource.Unauthorized());

        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);

        var scenario = Scenario;
        var response = MetaMessagesResource.For(scenario, _wamids.Next());
        if (response is null)
        {
            // Reached only if something set the scenario past the control endpoint's own check. Saying so
            // beats inventing an error code the Cloud API never returns.
            Record($"refused to answer '{scenario}', which the Cloud API has no equivalent of");
            return Results.StatusCode(StatusCodes.Status501NotImplemented);
        }

        Record($"whatsapp {Describe(body)} on {version}/{phoneNumberId}, answered {response.StatusCode} ({ProviderScenarios.Describe(scenario)})");

        return Write(response);
    }

    // Which shape left is the thing worth reading in a transcript: free text and an approved template are
    // different products, and only one of them works outside the 24h window.
    private static string Describe(string body)
    {
        try
        {
            using var payload = JsonDocument.Parse(body);
            var root = payload.RootElement;
            var type = root.TryGetProperty("type", out var kind) ? kind.GetString() : "unknown";
            var destination = root.TryGetProperty("to", out var to) ? Mask(to.GetString() ?? string.Empty) : "nobody";

            if (type is "template" && root.TryGetProperty("template", out var template))
                return $"template {template.GetProperty("name").GetString()} to {destination}";

            return $"{type} to {destination}";
        }
        catch (JsonException)
        {
            return $"{body.Length} bytes that are not json";
        }
    }

    // The double never checks the token, and does require a bearer to be present: an adapter that forgets
    // to authenticate has to fail here the same way it would fail against Meta.
    private static bool HasCredential(HttpRequest request)
    {
        if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization.ToString(), out var header))
            return false;

        return string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(header.Parameter);
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
        destination.Length <= 6 ? destination : string.Concat(destination.AsSpan(0, 6), "...", destination.AsSpan(destination.Length - 2));
}
