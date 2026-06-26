using Hiram.Domain.Webhooks;

namespace Hiram.UnitTests.Webhooks;

public class WebhookEndpointTests
{
    private static WebhookEndpoint CreateValid() => new(
        id: Guid.NewGuid(),
        tenantId: Guid.NewGuid(),
        url: "https://tenant.example.com/hooks",
        secretProtected: "protected:secret",
        createdAtUtc: DateTimeOffset.UnixEpoch);

    [Fact]
    public void Constructor_StoresValues()
    {
        var endpoint = CreateValid();

        Assert.Equal("https://tenant.example.com/hooks", endpoint.Url);
        Assert.Equal("protected:secret", endpoint.SecretProtected);
    }

    [Fact]
    public void Constructor_Throws_WhenTenantIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new WebhookEndpoint(
            Guid.NewGuid(), Guid.Empty, "https://tenant.example.com/hooks", "s", DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("not-a-uri")]
    [InlineData("ftp://tenant.example.com")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenUrlInvalid(string url)
    {
        Assert.Throws<ArgumentException>(() => new WebhookEndpoint(
            Guid.NewGuid(), Guid.NewGuid(), url, "s", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Constructor_Throws_WhenSecretIsBlank()
    {
        Assert.Throws<ArgumentException>(() => new WebhookEndpoint(
            Guid.NewGuid(), Guid.NewGuid(), "https://tenant.example.com/hooks", "  ", DateTimeOffset.UnixEpoch));
    }
}
