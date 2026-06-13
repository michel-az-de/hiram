using System.Text;
using Hiram.Api.Authentication;
using Hiram.Application.Notifications;
using Hiram.Contracts;
using Hiram.Domain.Notifications;
using Hiram.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc;

namespace Hiram.Api.Notifications;

internal static class NotificationEndpoints
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/notifications", SubmitAsync).WithTags("Notifications");
        app.MapGet("/v1/notifications", ListAsync).WithTags("Notifications");
        app.MapGet("/v1/notifications/{id:guid}", GetByIdAsync).WithTags("Notifications");

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

        SubmitNotificationResult result;
        try
        {
            result = await submit.SubmitAsync(command, cancellationToken);
        }
        catch (DuplicateIdempotencyKeyException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Idempotency conflict",
                detail: "The idempotency key conflicts with an existing request that could not be resolved.");
        }

        HiramDiagnostics.NotificationsAccepted.Add(1);

        if (result.Replayed)
            response.Headers["Idempotency-Replayed"] = "true";

        var body = new NotificationAccepted(result.NotificationId, ToWire(result.Status));
        return Results.Accepted($"/v1/notifications/{result.NotificationId}", body);
    }

    private static async Task<IResult> ListAsync(
        string? status,
        string? channel,
        DateTimeOffset? since,
        DateTimeOffset? until,
        string? cursor,
        int? limit,
        INotificationReader reader,
        TenantContext tenant,
        CancellationToken cancellationToken)
    {
        NotificationStatus? statusFilter = null;
        if (status is not null && (statusFilter = ParseStatus(status)) is null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Unknown status."] });

        NotificationChannel? channelFilter = null;
        if (channel is not null && (channelFilter = ParseChannel(channel)) is null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["channel"] = ["Channel must be one of: email."] });

        DateTimeOffset? cursorCreatedAt = null;
        Guid? cursorId = null;
        if (cursor is not null)
        {
            if (!TryDecodeCursor(cursor, out var at, out var id))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["cursor"] = ["Invalid cursor."] });
            cursorCreatedAt = at;
            cursorId = id;
        }

        var pageSize = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var query = new NotificationQuery(tenant.TenantId, statusFilter, channelFilter, since, until, cursorCreatedAt, cursorId, pageSize + 1);
        var rows = await reader.QueryAsync(query, cancellationToken);

        var hasMore = rows.Count > pageSize;
        var page = rows.Take(pageSize).ToList();
        var nextCursor = hasMore ? EncodeCursor(page[^1].CreatedAtUtc, page[^1].Id) : null;

        return Results.Ok(new NotificationPage(page.Select(ToSummary).ToList(), nextCursor));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        INotificationReader reader,
        TenantContext tenant,
        CancellationToken cancellationToken)
    {
        var notification = await reader.FindAsync(tenant.TenantId, id, cancellationToken);
        if (notification is null)
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Notification not found");

        var attempts = await reader.AttemptsAsync(tenant.TenantId, id, cancellationToken);
        var view = new NotificationDetailResponse(
            notification.Id,
            notification.Channel.ToString().ToLowerInvariant(),
            notification.Recipient,
            notification.Subject,
            ToWire(notification.Status),
            notification.CreatedAtUtc,
            attempts.Select(ToAttemptView).ToList());

        return Results.Ok(view);
    }

    private static NotificationResponse ToSummary(NotificationRequest notification) =>
        new(
            notification.Id,
            notification.Channel.ToString().ToLowerInvariant(),
            notification.Recipient,
            notification.Subject,
            ToWire(notification.Status),
            notification.CreatedAtUtc);

    private static DeliveryAttemptView ToAttemptView(DeliveryAttempt attempt) =>
        new(
            attempt.AttemptNumber,
            attempt.Provider,
            ToWire(attempt.Outcome),
            attempt.Error,
            attempt.Duration.TotalMilliseconds,
            attempt.Shadowed,
            attempt.PayloadHash,
            attempt.CreatedAtUtc);

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

    private static NotificationStatus? ParseStatus(string status) =>
        status.Trim().ToLowerInvariant() switch
        {
            "accepted" => NotificationStatus.Accepted,
            "queued" => NotificationStatus.Queued,
            "sending" => NotificationStatus.Sending,
            "sent" => NotificationStatus.Sent,
            "failed" => NotificationStatus.Failed,
            "suppressed" => NotificationStatus.Suppressed,
            _ => null
        };

    private static string ToWire(NotificationStatus status) => status.ToString().ToLowerInvariant();

    private static string ToWire(DeliveryOutcome outcome) => outcome switch
    {
        DeliveryOutcome.Sent => "sent",
        DeliveryOutcome.TransientFailure => "transient_failure",
        DeliveryOutcome.PermanentFailure => "permanent_failure",
        DeliveryOutcome.ShadowWouldSend => "shadow_would_send",
        _ => outcome.ToString().ToLowerInvariant()
    };

    private static string EncodeCursor(DateTimeOffset createdAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{createdAt.UtcTicks}:{id}"));

    private static bool TryDecodeCursor(string cursor, out DateTimeOffset createdAt, out Guid id)
    {
        createdAt = default;
        id = default;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var separator = decoded.IndexOf(':');
            if (separator <= 0
                || !long.TryParse(decoded[..separator], out var ticks)
                || !Guid.TryParse(decoded[(separator + 1)..], out id))
                return false;

            createdAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
