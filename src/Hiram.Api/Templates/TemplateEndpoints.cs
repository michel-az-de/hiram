using Hiram.Api.Authentication;
using Hiram.Application.Abstractions;
using Hiram.Application.Templates;
using Hiram.Contracts;
using Hiram.Domain.Notifications;
using Hiram.Domain.Templates;
using Microsoft.AspNetCore.Mvc;

namespace Hiram.Api.Templates;

internal static class TemplateEndpoints
{
    public static IEndpointRouteBuilder MapTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/templates", CreateAsync).WithTags("Templates");
        app.MapGet("/v1/templates", ListAsync).WithTags("Templates");
        app.MapGet("/v1/templates/{id:guid}", GetByIdAsync).WithTags("Templates");
        app.MapPut("/v1/templates/{id:guid}", UpdateAsync).WithTags("Templates");
        app.MapPost("/v1/templates/{id:guid}/approve", ApproveAsync).WithTags("Templates");
        app.MapDelete("/v1/templates/{id:guid}", DeleteAsync).WithTags("Templates");

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateTemplateRequest request,
        ITemplateStore store,
        ITemplateRenderer renderer,
        TenantContext tenant,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var channel = ParseChannel(request.Channel);
        var errors = new Dictionary<string, string[]>();
        if (channel is null)
            errors[nameof(request.Channel)] = ["Channel must be one of: email, sms, whatsapp."];
        else
            ValidateSubject(renderer, channel.Value, request.Subject, errors);
        if (string.IsNullOrWhiteSpace(request.Name))
            errors[nameof(request.Name)] = ["Name is required."];
        ValidateBody(renderer, request.Body, errors);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var template = new Template(
            Guid.NewGuid(), tenant.TenantId, channel!.Value, request.Name, Normalize(request.Subject), request.Body, clock.UtcNow);
        try
        {
            await store.AddAsync(template, cancellationToken);
        }
        catch (DuplicateTemplateNameException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Template already exists",
                detail: "A template with this name already exists for the channel.");
        }

        return Results.Created($"/v1/templates/{template.Id}", ToResponse(template));
    }

    private static async Task<IResult> ListAsync(
        ITemplateStore store, TenantContext tenant, CancellationToken cancellationToken)
    {
        var templates = await store.ListAsync(tenant.TenantId, cancellationToken);
        return Results.Ok(templates.Select(ToResponse).ToList());
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id, ITemplateStore store, TenantContext tenant, CancellationToken cancellationToken)
    {
        var template = await store.GetAsync(tenant.TenantId, id, cancellationToken);
        return template is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Template not found")
            : Results.Ok(ToResponse(template));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateTemplateRequest request,
        ITemplateStore store,
        ITemplateRenderer renderer,
        TenantContext tenant,
        IClock clock,
        CancellationToken cancellationToken)
    {
        // Whether a subject is allowed depends on the channel, and only the stored template knows it.
        var existing = await store.GetAsync(tenant.TenantId, id, cancellationToken);
        if (existing is null)
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Template not found");

        var errors = new Dictionary<string, string[]>();
        ValidateSubject(renderer, existing.Channel, request.Subject, errors);
        ValidateBody(renderer, request.Body, errors);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var updated = await store.UpdateAsync(
            tenant.TenantId, id, Normalize(request.Subject), request.Body, clock.UtcNow, cancellationToken);
        if (!updated)
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Template not found");

        var template = await store.GetAsync(tenant.TenantId, id, cancellationToken);
        return Results.Ok(ToResponse(template!));
    }

    private static async Task<IResult> ApproveAsync(
        Guid id, ITemplateStore store, TenantContext tenant, CancellationToken cancellationToken)
    {
        var approved = await store.ApproveAsync(tenant.TenantId, id, cancellationToken);
        return approved
            ? Results.NoContent()
            : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Template not found");
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, ITemplateStore store, TenantContext tenant, CancellationToken cancellationToken)
    {
        var deleted = await store.DeleteAsync(tenant.TenantId, id, cancellationToken);
        return deleted
            ? Results.NoContent()
            : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Template not found");
    }

    // The channel decides whether a subject belongs here at all: email renders one as the subject line,
    // SMS and WhatsApp have nowhere to put it, so accepting one there would store a value that never
    // reaches anyone.
    private static void ValidateSubject(
        ITemplateRenderer renderer, NotificationChannel channel, string? subject, Dictionary<string, string[]> errors)
    {
        if (!NotificationRequest.RequiresSubject(channel))
        {
            if (!string.IsNullOrWhiteSpace(subject))
                errors["subject"] = [$"Subject must be omitted on the {Wire(channel)} channel."];
            return;
        }

        if (string.IsNullOrWhiteSpace(subject))
            errors["subject"] = ["Subject is required."];
        else if (!renderer.TryValidate(subject, out var subjectError))
            errors["subject"] = [$"Template syntax error: {subjectError}"];
    }

    private static void ValidateBody(ITemplateRenderer renderer, string body, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(body))
            errors["body"] = ["Body is required."];
        else if (!renderer.TryValidate(body, out var bodyError))
            errors["body"] = [$"Template syntax error: {bodyError}"];
    }

    private static TemplateResponse ToResponse(Template template) =>
        new(
            template.Id,
            Wire(template.Channel),
            template.Name,
            template.Subject,
            template.Body,
            template.CreatedAtUtc,
            template.UpdatedAtUtc);

    private static NotificationChannel? ParseChannel(string? channel) =>
        channel?.Trim().ToLowerInvariant() switch
        {
            "email" => NotificationChannel.Email,
            "sms" => NotificationChannel.Sms,
            "whatsapp" => NotificationChannel.WhatsApp,
            _ => null
        };

    private static string Wire(NotificationChannel channel) => channel.ToString().ToLowerInvariant();

    // A blank subject is an absent one. Storing the whitespace instead would leave a value the renderer
    // would then render onto a channel that has nowhere to put it.
    private static string? Normalize(string? subject) => string.IsNullOrWhiteSpace(subject) ? null : subject;
}
