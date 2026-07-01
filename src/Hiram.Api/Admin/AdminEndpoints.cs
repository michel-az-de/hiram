using Hiram.Api.Authentication;
using Hiram.Application.Abstractions;
using Hiram.Application.Tenancy;
using Hiram.Domain.Tenants;

namespace Hiram.Api.Admin;

// Provisional operator surface until the Portal (F5). Guarded by a single shared X-Admin-Key from
// configuration, not by tenant API keys.
internal static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin").WithTags("Admin (provisional)").AddEndpointFilter(AdminKeyFilter.Require);
        admin.MapPost("/tenants", CreateTenantAsync);
        admin.MapPost("/api-keys", CreateApiKeyAsync);
        admin.MapDelete("/api-keys/{id:guid}", RevokeApiKeyAsync);

        return app;
    }

    private static async Task<IResult> CreateTenantAsync(
        CreateTenantRequest request,
        ITenantStore tenants,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });

        var mode = ParseDeliveryMode(request.DeliveryMode);
        if (mode is null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["deliveryMode"] = ["Delivery mode must be one of: live, shadow."] });

        var tenant = new Tenant(Guid.NewGuid(), request.Name, mode.Value, clock.UtcNow);
        await tenants.AddAsync(tenant, cancellationToken);

        var body = new TenantCreated(tenant.Id, tenant.Name, tenant.DeliveryMode.ToString().ToLowerInvariant());
        return Results.Created($"/v1/admin/tenants/{tenant.Id}", body);
    }

    private static async Task<IResult> CreateApiKeyAsync(
        CreateApiKeyRequest request,
        ITenantStore tenants,
        IApiKeyStore apiKeys,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["tenantId"] = ["Tenant id is required."] });
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });

        if (!await tenants.ExistsAsync(request.TenantId, cancellationToken))
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Tenant not found");

        var issued = ApiKeyIssuer.Issue(request.TenantId, request.Name, clock.UtcNow);
        await apiKeys.AddAsync(issued.ApiKey, cancellationToken);

        var body = new ApiKeyCreated(
            issued.ApiKey.Id,
            issued.ApiKey.TenantId,
            issued.ApiKey.Name,
            issued.ClearKey,
            issued.ApiKey.KeyPrefix);

        return Results.Created($"/v1/admin/api-keys/{issued.ApiKey.Id}", body);
    }

    private static async Task<IResult> RevokeApiKeyAsync(
        Guid id,
        IApiKeyStore apiKeys,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var revoked = await apiKeys.RevokeAsync(id, clock.UtcNow, cancellationToken);
        return revoked
            ? Results.NoContent()
            : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Api key not found");
    }

    private static DeliveryMode? ParseDeliveryMode(string? mode) =>
        mode?.Trim().ToLowerInvariant() switch
        {
            "live" => DeliveryMode.Live,
            "shadow" => DeliveryMode.Shadow,
            _ => null
        };
}
