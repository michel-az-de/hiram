using Hiram.Api.Authentication;
using Hiram.Application.Abstractions;
using Hiram.Application.Tenancy;
using Hiram.Domain.Tenants;

namespace Hiram.Api.Demo;

// Dev-only helper so a presenter can provision a demo tenant and a fresh api key without touching the
// operator X-Admin-Key on stage. The tenant is idempotent under a well known id and lives in shadow
// mode, so the whole pipeline runs without sending real email. Mapped only inside the development gate.
internal static class DemoEndpoints
{
    private static readonly Guid DemoTenantId = new("0de70000-0000-0000-0000-000000000001");
    private const string DemoTenantName = "hiram-demo";
    private const string DemoApiKeyName = "demo-console";

    public static IEndpointRouteBuilder MapDemoBootstrapEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/demo/bootstrap", BootstrapAsync)
            .WithTags("Demo (development only)")
            .AddEndpointFilter(AdminKeyFilter.Require);

        return app;
    }

    private static async Task<IResult> BootstrapAsync(
        ITenantStore tenants,
        IApiKeyStore apiKeys,
        IClock clock,
        CancellationToken cancellationToken)
    {
        // Check then create under a fixed id keeps this idempotent for the sequential calls a presenter
        // makes. Two truly concurrent first-time bootstraps would race and the second insert would hit
        // the tenant unique key, surfacing as a 500, which is acceptable for this dev-only helper.
        if (!await tenants.ExistsAsync(DemoTenantId, cancellationToken))
        {
            var tenant = new Tenant(DemoTenantId, DemoTenantName, DeliveryMode.Shadow, clock.UtcNow);
            await tenants.AddAsync(tenant, cancellationToken);
        }

        // A fresh key on every bootstrap: the clear value exists only at issue time, so we hand back a
        // new one rather than trying to recover a previous key.
        var issued = ApiKeyIssuer.Issue(DemoTenantId, DemoApiKeyName, clock.UtcNow);
        await apiKeys.AddAsync(issued.ApiKey, cancellationToken);

        var body = new DemoBootstrapResult(
            DemoTenantId,
            DemoTenantName,
            DeliveryMode.Shadow.ToString().ToLowerInvariant(),
            issued.ApiKey.Id,
            issued.ApiKey.Name,
            issued.ClearKey,
            issued.ApiKey.KeyPrefix);

        return Results.Ok(body);
    }
}
