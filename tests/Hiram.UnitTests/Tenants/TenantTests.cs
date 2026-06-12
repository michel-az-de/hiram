using Hiram.Domain.Tenants;

namespace Hiram.UnitTests.Tenants;

public class TenantTests
{
    [Fact]
    public void Constructor_StoresProvidedValues()
    {
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UnixEpoch;

        var tenant = new Tenant(id, "easystok", DeliveryMode.Shadow, createdAt);

        Assert.Equal(id, tenant.Id);
        Assert.Equal("easystok", tenant.Name);
        Assert.Equal(DeliveryMode.Shadow, tenant.DeliveryMode);
        Assert.Equal(createdAt, tenant.CreatedAtUtc);
    }

    [Fact]
    public void Constructor_Throws_WhenIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            new Tenant(Guid.Empty, "easystok", DeliveryMode.Live, DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenNameIsBlank(string name)
    {
        Assert.Throws<ArgumentException>(() =>
            new Tenant(Guid.NewGuid(), name, DeliveryMode.Live, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Constructor_Throws_WhenDeliveryModeIsUnknown()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Tenant(Guid.NewGuid(), "easystok", (DeliveryMode)99, DateTimeOffset.UnixEpoch));
    }
}
