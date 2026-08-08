namespace Hiram.Contracts;

/// <summary>
/// Subject is null on channels that render no subject line, such as SMS.
/// </summary>
public sealed record UpdateTemplateRequest(string? Subject, string Body);
