using Hiram.Domain.Notifications;

namespace Hiram.Application.Notifications;

// Segments is the number of SMS segments the accepted body costs, and null on every other channel. The
// carrier bills by segment, so an emitter that only learns the count from the invoice learns it too late.
public sealed record SubmitNotificationResult(
    Guid NotificationId, NotificationStatus Status, bool Replayed = false, int? Segments = null);
