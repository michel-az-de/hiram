namespace Hiram.Simulator.Providers;

// The web host every double is served from. It lives outside the doubles so adding a provider is a class
// that maps its own routes, not another copy of builder configuration and of the control endpoint.
public static class ProviderDoubleHost
{
    public static WebApplication Build(IProviderDouble provider, Uri address)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseUrls(address.ToString());

        var app = builder.Build();
        provider.MapInto(app);

        // Lets a script flip the answer between acts without restarting the process. A scenario the
        // provider cannot produce is refused here rather than answered with something invented.
        app.MapPost("/_control/scenario", (ScenarioChange change) =>
        {
            if (!ProviderScenarios.TryParse(change.Scenario, out var parsed))
                return Results.BadRequest(new { error = $"Unknown scenario '{change.Scenario}'." });

            if (!provider.Supports(parsed))
                return Results.BadRequest(new
                {
                    error = $"{provider.Name} has no equivalent of '{parsed}'."
                });

            provider.Scenario = parsed;
            return Results.Ok(new { scenario = parsed.ToString() });
        });

        return app;
    }

    private sealed record ScenarioChange(string Scenario);
}
