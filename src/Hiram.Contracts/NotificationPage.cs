namespace Hiram.Contracts;

/// <summary>A page of notifications. <see cref="NextCursor"/> is null when there are no more results.</summary>
public sealed record NotificationPage(IReadOnlyList<NotificationResponse> Items, string? NextCursor);
