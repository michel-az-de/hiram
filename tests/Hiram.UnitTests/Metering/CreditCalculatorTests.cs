using Hiram.Application.Metering;
using Hiram.Domain.Notifications;

namespace Hiram.UnitTests.Metering;

public class CreditCalculatorTests
{
    private static CreditCalculator Build(long emailBase = 2, long perKb = 1, long defaultBase = 1) =>
        new(new CreditRates(new Dictionary<NotificationChannel, long> { [NotificationChannel.Email] = emailBase }, defaultBase, perKb));

    [Fact]
    public void Cost_IsChannelBase_WhenPayloadEmpty()
    {
        Assert.Equal(2, Build().Cost(NotificationChannel.Email, 0));
    }

    [Fact]
    public void Cost_AddsCeilingOfKilobytes()
    {
        // 1500 bytes rounds up to 2 KB: base 2 plus 2 times 1.
        Assert.Equal(4, Build(emailBase: 2, perKb: 1).Cost(NotificationChannel.Email, 1500));
    }

    [Fact]
    public void Cost_FallsBackToDefaultBase_ForUnconfiguredChannel()
    {
        Assert.Equal(3, Build(defaultBase: 3).Cost(NotificationChannel.Push, 0));
    }

    [Fact]
    public void Cost_IsAtLeastOne_WhenRatesAreZero()
    {
        var calculator = new CreditCalculator(new CreditRates(new Dictionary<NotificationChannel, long>(), DefaultBase: 0, PerKilobyte: 0));

        Assert.Equal(1, calculator.Cost(NotificationChannel.Email, 0));
    }
}
