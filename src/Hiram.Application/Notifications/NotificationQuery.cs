using Hiram.Domain.Notifications;

namespace Hiram.Application.Notifications;

// Cursor pagination keys on (created_at, id): the id breaks ties so a page boundary is stable even when
// two notifications share a timestamp.
public sealed record NotificationQuery(
    Guid TenantId,
    NotificationStatus? Status,
    NotificationChannel? Channel,
    DateTimeOffset? Since,
    DateTimeOffset? Until,
    DateTimeOffset? CursorCreatedAtUtc,
    Guid? CursorId,
    int Limit);
