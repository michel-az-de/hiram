using Hiram.Domain.Metering;

namespace Hiram.UnitTests.Metering;

public class CreditLedgerEntryTests
{
    [Fact]
    public void Debit_StoresNegativeAmount()
    {
        var tenantId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();

        var entry = CreditLedgerEntry.Debit(tenantId, notificationId, 5, "notification:accepted", DateTimeOffset.UnixEpoch);

        Assert.Equal(-5, entry.Amount);
        Assert.Equal(notificationId, entry.NotificationId);
        Assert.Equal("notification:accepted", entry.Reason);
    }

    [Fact]
    public void Debit_Throws_WhenCostNotPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreditLedgerEntry.Debit(Guid.NewGuid(), Guid.NewGuid(), 0, "r", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Constructor_Throws_WhenAmountIsZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CreditLedgerEntry(Guid.NewGuid(), Guid.NewGuid(), null, 0, "r", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Constructor_Throws_WhenTenantIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            new CreditLedgerEntry(Guid.NewGuid(), Guid.Empty, null, -1, "r", DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenReasonIsBlank(string reason)
    {
        Assert.Throws<ArgumentException>(() =>
            new CreditLedgerEntry(Guid.NewGuid(), Guid.NewGuid(), null, -1, reason, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Constructor_AllowsNullNotificationId_ForTopUps()
    {
        var entry = new CreditLedgerEntry(Guid.NewGuid(), Guid.NewGuid(), null, 100, "top_up", DateTimeOffset.UnixEpoch);

        Assert.Null(entry.NotificationId);
        Assert.Equal(100, entry.Amount);
    }
}
