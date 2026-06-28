using Hiram.Api.Authentication;
using Hiram.Application.Abstractions;
using Hiram.Application.Blocks;
using Hiram.Contracts;
using Hiram.Domain.Blocks;
using Hiram.Domain.Notifications;

namespace Hiram.Api.Blocks;

internal static class BlockEndpoints
{
    public static IEndpointRouteBuilder MapBlockEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/blocks", CreateAsync).WithTags("Blocks");
        app.MapDelete("/v1/blocks/{id:guid}", RemoveAsync).WithTags("Blocks");
        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateBlockRequest request,
        IBlockStore store,
        TenantContext tenant,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["reason is required."] });

        NotificationChannel? channel = null;
        if (request.Channel is not null)
        {
            channel = ParseChannel(request.Channel);
            if (channel is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["channel"] = ["channel must be one of: email, push."] });
        }

        var block = new Block(Guid.NewGuid(), tenant.TenantId, channel, request.Reason, clock.UtcNow, request.ExpiresAtUtc);
        await store.AddAsync(block, cancellationToken);

        return Results.Created($"/v1/blocks/{block.Id}", new BlockCreated(block.Id));
    }

    private static async Task<IResult> RemoveAsync(
        Guid id, IBlockStore store, TenantContext tenant, IClock clock, CancellationToken cancellationToken)
    {
        var removed = await store.RemoveAsync(tenant.TenantId, id, clock.UtcNow, cancellationToken);
        return removed
            ? Results.NoContent()
            : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Block not found");
    }

    private static NotificationChannel? ParseChannel(string channel) =>
        channel.Trim().ToLowerInvariant() switch
        {
            "email" => NotificationChannel.Email,
            "push" => NotificationChannel.Push,
            _ => null
        };

    private sealed record BlockCreated(Guid Id);
}
