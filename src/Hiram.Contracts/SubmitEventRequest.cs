namespace Hiram.Contracts;

public sealed record SubmitEventRequest(
    string EventType,
    string EventId,
    long EmissionSeq,
    EventRecipient Recipient,
    string? LogicalAlertId = null,
    string? Timezone = null,
    IReadOnlyDictionary<string, object?>? Data = null);
