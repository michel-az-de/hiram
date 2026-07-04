namespace Hiram.Api.Admin;

internal sealed record RoutineCreated(
    Guid Id,
    Guid? TenantId,
    string EventType,
    string TemplateName,
    IReadOnlyList<string> Channels,
    string Category,
    bool Active);
