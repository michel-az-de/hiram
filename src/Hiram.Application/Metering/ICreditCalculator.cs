using Hiram.Domain.Notifications;

namespace Hiram.Application.Metering;

public interface ICreditCalculator
{
    long Cost(NotificationChannel channel, int payloadBytes);
}
