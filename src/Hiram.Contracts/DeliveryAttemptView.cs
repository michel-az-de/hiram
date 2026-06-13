namespace Hiram.Contracts;

public sealed record DeliveryAttemptView(
    int AttemptNumber,
    string Provider,
    string Outcome,
    string? Error,
    double DurationMs,
    bool Shadowed,
    string? PayloadHash,
    DateTimeOffset CreatedAtUtc);
