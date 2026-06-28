namespace Hiram.Contracts;

public sealed record CreateBlockRequest(string? Channel, string Reason, DateTimeOffset? ExpiresAtUtc);
