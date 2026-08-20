namespace Hiram.Contracts;

/// <summary>
/// Segments is how many SMS segments the accepted body costs, and null on every other channel. The
/// carrier bills by segment, and one character outside GSM-7 drops the limit from 160 to 70.
/// </summary>
public sealed record NotificationAccepted(Guid Id, string Status, int? Segments = null);
