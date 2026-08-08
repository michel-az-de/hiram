namespace Hiram.Contracts;

/// <summary>
/// Subject is null on channels that render no subject line, such as SMS.
/// </summary>
public sealed record TemplateResponse(
    Guid Id,
    string Channel,
    string Name,
    string? Subject,
    string Body,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
