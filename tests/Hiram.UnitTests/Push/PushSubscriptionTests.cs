using Hiram.Domain.Push;

namespace Hiram.UnitTests.Push;

public class PushSubscriptionTests
{
    private static PushSubscription CreateValid() => new(
        id: Guid.NewGuid(),
        tenantId: Guid.NewGuid(),
        endpoint: "https://push.example.com/abc",
        p256dh: "BNc...",
        auth: "k9...",
        createdAtUtc: DateTimeOffset.UnixEpoch);

    [Fact]
    public void Constructor_StoresValues()
    {
        var subscription = CreateValid();

        Assert.Equal("https://push.example.com/abc", subscription.Endpoint);
        Assert.Equal("BNc...", subscription.P256dh);
        Assert.Equal("k9...", subscription.Auth);
    }

    [Fact]
    public void Constructor_Throws_WhenTenantIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new PushSubscription(
            Guid.NewGuid(), Guid.Empty, "https://push.example.com/abc", "p", "a", DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-uri")]
    public void Constructor_Throws_WhenEndpointInvalid(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => new PushSubscription(
            Guid.NewGuid(), Guid.NewGuid(), endpoint, "p", "a", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Constructor_Throws_WhenAuthIsBlank()
    {
        Assert.Throws<ArgumentException>(() => new PushSubscription(
            Guid.NewGuid(), Guid.NewGuid(), "https://push.example.com/abc", "p", "  ", DateTimeOffset.UnixEpoch));
    }
}
