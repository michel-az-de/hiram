using System.Buffers.Text;
using System.Security.Cryptography;
using Hiram.Api.Authentication;
using Hiram.Application.Abstractions;
using Hiram.Application.Tenancy;
using Hiram.Application.Webhooks;
using Hiram.Contracts;
using Hiram.Domain.Webhooks;

namespace Hiram.Api.Webhooks;

internal static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/webhooks", RegisterAsync).WithTags("Webhooks");
        app.MapGet("/v1/webhooks", ListAsync).WithTags("Webhooks");
        app.MapDelete("/v1/webhooks/{id:guid}", DeleteAsync).WithTags("Webhooks");

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterWebhookRequest request,
        IWebhookEndpointStore store,
        ISecretProtector protector,
        TenantContext tenant,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        WebhookEndpoint endpoint;
        try
        {
            endpoint = new WebhookEndpoint(Guid.NewGuid(), tenant.TenantId, request.Url, protector.Protect(secret), clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["url"] = [ex.Message] });
        }

        try
        {
            await store.AddAsync(endpoint, cancellationToken);
        }
        catch (DuplicateWebhookEndpointException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Webhook already registered",
                detail: "This url is already registered as a webhook for the tenant.");
        }

        return Results.Created($"/v1/webhooks/{endpoint.Id}", new WebhookRegistered(endpoint.Id, endpoint.Url, secret));
    }

    private static async Task<IResult> ListAsync(
        IWebhookEndpointStore store, TenantContext tenant, CancellationToken cancellationToken)
    {
        var endpoints = await store.ListAsync(tenant.TenantId, cancellationToken);
        return Results.Ok(endpoints.Select(e => new WebhookResponse(e.Id, e.Url, e.CreatedAtUtc)).ToList());
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, IWebhookEndpointStore store, TenantContext tenant, CancellationToken cancellationToken)
    {
        var deleted = await store.DeleteAsync(tenant.TenantId, id, cancellationToken);
        return deleted
            ? Results.NoContent()
            : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Webhook not found");
    }
}
