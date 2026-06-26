using Hiram.Api.Authentication;
using Hiram.Application.Abstractions;
using Hiram.Application.Push;
using Hiram.Contracts;
using Hiram.Domain.Push;

namespace Hiram.Api.Push;

internal static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/push-subscriptions", RegisterAsync).WithTags("Push");
        app.MapGet("/v1/push-subscriptions", ListAsync).WithTags("Push");
        app.MapDelete("/v1/push-subscriptions/{id:guid}", DeleteAsync).WithTags("Push");
        app.MapGet("/v1/push/vapid-public-key", GetVapidPublicKey).WithTags("Push");

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterPushSubscriptionRequest request,
        IPushSubscriptionStore store,
        TenantContext tenant,
        IClock clock,
        CancellationToken cancellationToken)
    {
        PushSubscription subscription;
        try
        {
            subscription = new PushSubscription(
                Guid.NewGuid(), tenant.TenantId, request.Endpoint, request.P256dh, request.Auth, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["subscription"] = [ex.Message] });
        }

        try
        {
            await store.AddAsync(subscription, cancellationToken);
        }
        catch (DuplicateSubscriptionException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Subscription already registered",
                detail: "This endpoint is already registered for the tenant.");
        }

        return Results.Created($"/v1/push-subscriptions/{subscription.Id}", ToResponse(subscription));
    }

    private static async Task<IResult> ListAsync(
        IPushSubscriptionStore store, TenantContext tenant, CancellationToken cancellationToken)
    {
        var subscriptions = await store.ListAsync(tenant.TenantId, cancellationToken);
        return Results.Ok(subscriptions.Select(ToResponse).ToList());
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, IPushSubscriptionStore store, TenantContext tenant, CancellationToken cancellationToken)
    {
        var deleted = await store.DeleteAsync(tenant.TenantId, id, cancellationToken);
        return deleted
            ? Results.NoContent()
            : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Subscription not found");
    }

    private static IResult GetVapidPublicKey(PushVapidOptions vapid) =>
        string.IsNullOrEmpty(vapid.PublicKey)
            ? Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Push is not configured",
                detail: "No VAPID public key is configured on this deployment.")
            : Results.Ok(new VapidPublicKeyResponse(vapid.PublicKey));

    private static PushSubscriptionResponse ToResponse(PushSubscription subscription) =>
        new(subscription.Id, subscription.Endpoint, subscription.CreatedAtUtc);
}
