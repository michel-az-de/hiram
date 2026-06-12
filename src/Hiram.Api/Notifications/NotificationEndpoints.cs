using Hiram.Api.Authentication;
using Hiram.Application.Notifications;
using Hiram.Contracts;
using Hiram.Domain.Notifications;
using Hiram.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc;

namespace Hiram.Api.Notifications;

internal static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/notifications", SubmitAsync);
        app.MapGet("/v1/notifications/{id:guid}", GetByIdAsync);

        return app;
    }

    private static async Task<IResult> SubmitAsync(
        SubmitNotificationRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ISubmitNotification submit,
        TenantContext tenant,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        idempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (idempotencyKey is { Length: > 255 })
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["idempotencyKey"] = ["Idempotency-Key must be at most 255 characters."]
            });

        var channel = ParseChannel(request.Channel)!.Value;
        var command = new SubmitNotificationCommand(tenant.TenantId, channel, request.Recipient, request.Subject, request.Body, idempotencyKey);

        var result = await submit.SubmitAsync(command, cancellationToken);
        HiramDiagnostics.NotificationsAccepted.Add(1);

        if (result.Replayed)
            response.Headers["Idempotency-Replayed"] = "true";

        var body = new NotificationAccepted(result.NotificationId, ToWire(result.Status));
        return Results.Accepted($"/v1/notifications/{result.NotificationId}", body);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        INotificationReader reader,
        CancellationToken cancellationToken)
    {
        var notification = await reader.FindAsync(id, cancellationToken);
        if (notification is null)
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Notification not found");

        var view = new NotificationResponse(
            notification.Id,
            notification.Channel.ToString().ToLowerInvariant(),
            notification.Recipient,
            notification.Subject,
            ToWire(notification.Status),
            notification.CreatedAtUtc);

        return Results.Ok(view);
    }

    private static Dictionary<string, string[]> Validate(SubmitNotificationRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (ParseChannel(request.Channel) is null)
            errors[nameof(request.Channel)] = ["Channel must be one of: email."];
        if (string.IsNullOrWhiteSpace(request.Recipient))
            errors[nameof(request.Recipient)] = ["Recipient is required."];
        if (string.IsNullOrWhiteSpace(request.Subject))
            errors[nameof(request.Subject)] = ["Subject is required."];
        if (string.IsNullOrWhiteSpace(request.Body))
            errors[nameof(request.Body)] = ["Body is required."];

        return errors;
    }

    private static NotificationChannel? ParseChannel(string? channel) =>
        channel?.Trim().ToLowerInvariant() switch
        {
            "email" => NotificationChannel.Email,
            _ => null
        };

    private static string ToWire(NotificationStatus status) => status.ToString().ToLowerInvariant();
}
