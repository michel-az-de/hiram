using Hiram.Domain.Notifications;
using Hiram.Domain.Routines;

namespace Hiram.Application.Consents;

// A consent state observed in the EasyStok store, sent to Hiram by the dual-write during shadow.
public sealed record ConsentSnapshot(
    Guid UserId,
    NotificationChannel Channel,
    NotificationCategory Category,
    bool OptIn,
    DateTimeOffset UpdatedAtUtc);
