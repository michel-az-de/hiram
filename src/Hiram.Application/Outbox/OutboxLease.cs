namespace Hiram.Application.Outbox;

public sealed record OutboxLease(
    Guid Id,
    Guid TenantId,
    string Type,
    string Payload,
    string? TraceParent,
    DateTimeOffset AvailableAt,
    DateTimeOffset LeaseUntil,
    int AttemptCount);
