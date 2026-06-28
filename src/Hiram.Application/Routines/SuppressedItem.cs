using Hiram.Domain.Notifications;
using Hiram.Domain.Routines;

namespace Hiram.Application.Routines;

public sealed record SuppressedItem(Routine Routine, NotificationChannel Channel, string Reason);
