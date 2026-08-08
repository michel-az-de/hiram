namespace Hiram.Contracts;

/// <summary>
/// TrialContent is true when the provider was sent approved content instead of the notification body,
/// which a trial account forces. The body on the notification is then not what was delivered.
/// </summary>
public sealed record DeliveryAttemptView(
    int AttemptNumber,
    string Provider,
    string Outcome,
    string? Error,
    double DurationMs,
    bool Shadowed,
    string? PayloadHash,
    DateTimeOffset CreatedAtUtc,
    bool TrialContent);
