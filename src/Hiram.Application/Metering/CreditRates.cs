using Hiram.Domain.Notifications;

namespace Hiram.Application.Metering;

public sealed record CreditRates(
    IReadOnlyDictionary<NotificationChannel, long> ChannelBase,
    long DefaultBase,
    long PerKilobyte);
