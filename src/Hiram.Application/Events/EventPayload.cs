namespace Hiram.Application.Events;

// The contact travels at emission time (ADR-018); the engine picks the address for the resolved channel.
public sealed record EventPayload(
    string? RecipientUserId,
    string? RecipientEmail,
    string? RecipientPhone,
    string? LogicalAlertId,
    string? Timezone,
    IReadOnlyDictionary<string, object?>? Data);
