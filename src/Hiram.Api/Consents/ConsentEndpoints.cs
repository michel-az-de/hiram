using Hiram.Api.Authentication;
using Hiram.Application.Abstractions;
using Hiram.Application.Consents;
using Hiram.Contracts;
using Hiram.Domain.Consents;
using Hiram.Domain.Notifications;
using Hiram.Domain.Routines;

namespace Hiram.Api.Consents;

internal static class ConsentEndpoints
{
    public static IEndpointRouteBuilder MapConsentEndpoints(this IEndpointRouteBuilder app)
    {
        // The EasyStok UI and worker write consent here (the dual-write target of ADR-018).
        app.MapPost("/v1/consent", UpsertAsync).WithTags("Consent");
        return app;
    }

    private static async Task<IResult> UpsertAsync(
        SetConsentRequest request,
        IConsentStore store,
        TenantContext tenant,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var channel = ParseChannel(request.Channel);
        var category = ParseCategory(request.Category);

        var errors = new Dictionary<string, string[]>();
        if (request.UserId == Guid.Empty)
            errors[nameof(request.UserId)] = ["userId is required."];
        if (channel is null)
            errors[nameof(request.Channel)] = ["channel must be one of: email, push, sms, whatsapp."];
        if (category is null)
            errors[nameof(request.Category)] = ["category must be one of: transactional, operational, marketing."];
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var consent = new Consent(
            Guid.NewGuid(), tenant.TenantId, request.UserId, channel!.Value, category!.Value, request.OptIn, clock.UtcNow);
        await store.UpsertAsync(consent, cancellationToken);

        return Results.NoContent();
    }

    private static NotificationChannel? ParseChannel(string? channel) =>
        channel?.Trim().ToLowerInvariant() switch
        {
            "email" => NotificationChannel.Email,
            "push" => NotificationChannel.Push,
            "sms" => NotificationChannel.Sms,
            "whatsapp" => NotificationChannel.WhatsApp,
            _ => null
        };

    private static NotificationCategory? ParseCategory(string? category) =>
        category?.Trim().ToLowerInvariant() switch
        {
            "transactional" => NotificationCategory.Transactional,
            "operational" => NotificationCategory.Operational,
            "marketing" => NotificationCategory.Marketing,
            _ => null
        };
}
