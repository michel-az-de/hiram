using Hiram.Domain.Notifications;

namespace Hiram.Application.Metering;

public sealed class CreditCalculator : ICreditCalculator
{
    private readonly CreditRates _rates;

    public CreditCalculator(CreditRates rates)
    {
        _rates = rates;
    }

    public long Cost(NotificationChannel channel, int payloadBytes)
    {
        var kilobytes = (payloadBytes + 1023) / 1024;
        var channelBase = _rates.ChannelBase.GetValueOrDefault(channel, _rates.DefaultBase);

        // A notification always costs at least one credit, even with empty payload and zeroed rates.
        return Math.Max(1, channelBase + kilobytes * _rates.PerKilobyte);
    }
}
