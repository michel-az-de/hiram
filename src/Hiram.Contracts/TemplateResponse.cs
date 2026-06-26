namespace Hiram.Contracts;

public sealed record TemplateResponse(
    Guid Id,
    string Channel,
    string Name,
    string Subject,
    string Body,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
