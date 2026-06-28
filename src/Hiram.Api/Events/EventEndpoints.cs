using Hiram.Api.Authentication;
using Hiram.Application.Events;
using Hiram.Contracts;

namespace Hiram.Api.Events;

internal static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/events", IngestAsync).WithTags("Events");
        return app;
    }

    private static async Task<IResult> IngestAsync(
        SubmitEventRequest request,
        IIngestEvent ingest,
        TenantContext tenant,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var payload = new EventPayload(
            request.Recipient.UserId,
            request.Recipient.Email,
            request.Recipient.Phone,
            request.LogicalAlertId,
            request.Timezone,
            request.Data);

        var command = new IngestEventCommand(
            tenant.TenantId, request.EventType.Trim(), request.EventId.Trim(), request.EmissionSeq, payload);

        var result = await ingest.IngestAsync(command, cancellationToken);

        if (result.Replayed)
            response.Headers["Idempotency-Replayed"] = "true";

        return Results.Accepted($"/v1/events/{result.Id}", new EventAccepted(result.Id, "accepted"));
    }

    private static Dictionary<string, string[]> Validate(SubmitEventRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.EventType))
            errors[nameof(request.EventType)] = ["eventType is required."];
        if (string.IsNullOrWhiteSpace(request.EventId))
            errors[nameof(request.EventId)] = ["eventId is required."];
        if (request.Recipient is null)
            errors[nameof(request.Recipient)] = ["recipient is required."];

        return errors;
    }
}
