using Hiram.Domain.Notifications;
using Hiram.Domain.Tenants;

namespace Hiram.UnitTests.Tenants;

public class TenantProviderConfigTests
{
    [Fact]
    public void Constructor_StoresProvidedValues()
    {
        var tenantId = Guid.NewGuid();
        var updatedAt = DateTimeOffset.UnixEpoch;

        var config = new TenantProviderConfig(
            tenantId, NotificationChannel.Email, "smtp", "{\"host\":\"localhost\"}", "protected-secret", updatedAt);

        Assert.Equal(tenantId, config.TenantId);
        Assert.Equal(NotificationChannel.Email, config.Channel);
        Assert.Equal("smtp", config.Provider);
        Assert.Equal("{\"host\":\"localhost\"}", config.Settings);
        Assert.Equal("protected-secret", config.SecretProtected);
        Assert.Equal(updatedAt, config.UpdatedAtUtc);
    }

    [Fact]
    public void Constructor_AllowsNullSecret()
    {
        var config = new TenantProviderConfig(
            Guid.NewGuid(), NotificationChannel.Email, "smtp", "{}", secretProtected: null, DateTimeOffset.UnixEpoch);

        Assert.Null(config.SecretProtected);
    }

    [Fact]
    public void Constructor_Throws_WhenTenantIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            new TenantProviderConfig(Guid.Empty, NotificationChannel.Email, "smtp", "{}", null, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Constructor_Throws_WhenChannelIsUnknown()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TenantProviderConfig(Guid.NewGuid(), (NotificationChannel)999, "smtp", "{}", null, DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenProviderIsBlank(string provider)
    {
        Assert.Throws<ArgumentException>(() =>
            new TenantProviderConfig(Guid.NewGuid(), NotificationChannel.Email, provider, "{}", null, DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenSettingsAreBlank(string settings)
    {
        Assert.Throws<ArgumentException>(() =>
            new TenantProviderConfig(Guid.NewGuid(), NotificationChannel.Email, "smtp", settings, null, DateTimeOffset.UnixEpoch));
    }
}
