using Hiram.Api.Authentication;
using Hiram.Application.Abstractions;
using Hiram.Application.Routines;
using Hiram.Application.Tenancy;
using Hiram.Domain.Notifications;
using Hiram.Domain.Routines;
using Hiram.Domain.Tenants;

namespace Hiram.Api.Admin;

// Operator surface guarded by a shared X-Admin-Key from configuration, not by tenant API keys.
internal static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin").WithTags("Admin").AddEndpointFilter(AdminKeyFilter.Require);
        admin.MapPost("/tenants", CreateTenantAsync);
        admin.MapPost("/api-keys", CreateApiKeyAsync);
        admin.MapDelete("/api-keys/{id:guid}", RevokeApiKeyAsync);
        admin.MapPost("/routines", CreateRoutineAsync);

        return app;
    }

    private static async Task<IResult> CreateRoutineAsync(
        CreateRoutineRequest request,
        ITenantStore tenants,
        IRoutineStore routines,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.EventType))
            errors[nameof(request.EventType)] = ["Event type is required."];
        if (string.IsNullOrWhiteSpace(request.TemplateName))
            errors[nameof(request.TemplateName)] = ["Template name is required."];

        var channels = ParseChannels(request.Channels);
        if (channels.Count == 0)
            errors[nameof(request.Channels)] = ["At least one valid channel is required (email, push, sms, whatsapp)."];

        var category = ParseCategory(request.Category);
        if (category is null)
            errors[nameof(request.Category)] = ["Category must be one of: transactional, operational, marketing."];

        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        Guid? tenantId = request.TenantId == Guid.Empty ? null : request.TenantId;
        if (tenantId is Guid id && !await tenants.ExistsAsync(id, cancellationToken))
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Tenant not found");

        var eventType = request.EventType.Trim();

        // Idempotent: re-running the provisioning script must not create duplicate routines.
        var existing = await routines.FindActiveAsync(tenantId, eventType, cancellationToken);
        if (existing is not null)
            return Results.Ok(ToResponse(existing));

        var routine = new Routine(
            Guid.NewGuid(), tenantId, eventType, request.TemplateName.Trim(), channels, category!.Value, request.Active);
        await routines.AddAsync(routine, cancellationToken);

        return Results.Created($"/v1/admin/routines/{routine.Id}", ToResponse(routine));
    }

    private static RoutineCreated ToResponse(Routine routine) =>
        new(
            routine.Id,
            routine.TenantId,
            routine.EventType,
            routine.TemplateName,
            routine.Channels.Select(c => c.ToString().ToLowerInvariant()).ToList(),
            routine.Category.ToString().ToLowerInvariant(),
            routine.Active);

    private static List<NotificationChannel> ParseChannels(IReadOnlyList<string>? channels)
    {
        if (channels is null)
            return [];

        var parsed = new List<NotificationChannel>();
        foreach (var channel in channels)
        {
            switch (channel?.Trim().ToLowerInvariant())
            {
                case "email":
                    parsed.Add(NotificationChannel.Email);
                    break;
                case "push":
                    parsed.Add(NotificationChannel.Push);
                    break;
                case "sms":
                    parsed.Add(NotificationChannel.Sms);
                    break;
                case "whatsapp":
                    parsed.Add(NotificationChannel.WhatsApp);
                    break;
                default:
                    return [];
            }
        }

        return parsed;
    }

    private static NotificationCategory? ParseCategory(string? category) =>
        category?.Trim().ToLowerInvariant() switch
        {
            "transactional" => NotificationCategory.Transactional,
            "operational" => NotificationCategory.Operational,
            "marketing" => NotificationCategory.Marketing,
            _ => null
        };

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
