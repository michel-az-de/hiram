using Hiram.Domain.Notifications;
using Hiram.Domain.Routines;

namespace Hiram.Application.Routines;

public sealed record FanoutItem(Routine Routine, NotificationChannel Channel, string TemplateName);
