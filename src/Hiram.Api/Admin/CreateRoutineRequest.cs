namespace Hiram.Api.Admin;

internal sealed record CreateRoutineRequest(
    Guid TenantId,
    string EventType,
    string TemplateName,
    IReadOnlyList<string> Channels,
    string Category,
    bool Active);
