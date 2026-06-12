using Hiram.Domain.Notifications;

namespace Hiram.Application.Notifications;

public sealed record SubmitNotificationResult(Guid NotificationId, NotificationStatus Status, bool Replayed = false);
