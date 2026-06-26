namespace Hiram.Contracts;

public sealed record DeadLetterView(
    string Reason,
    int AttemptCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReplayedAtUtc);
