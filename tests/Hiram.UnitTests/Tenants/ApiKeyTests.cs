using Hiram.Domain.Tenants;

namespace Hiram.UnitTests.Tenants;

public class ApiKeyTests
{
    private static ApiKey CreateValid() => new(
        id: Guid.NewGuid(),
        tenantId: Guid.NewGuid(),
        name: "easystok-server",
        keyHash: new string('a', 64),
        keyPrefix: "hk_live_",
        createdAtUtc: DateTimeOffset.UnixEpoch);

    [Fact]
    public void Constructor_StoresProvidedValues()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var key = new ApiKey(id, tenantId, "easystok-server", new string('b', 64), "hk_live_", DateTimeOffset.UnixEpoch);

        Assert.Equal(id, key.Id);
        Assert.Equal(tenantId, key.TenantId);
        Assert.Equal("easystok-server", key.Name);
        Assert.Equal(new string('b', 64), key.KeyHash);
        Assert.Equal("hk_live_", key.KeyPrefix);
    }

    [Fact]
    public void Constructor_LeavesKeyActive_WithNoRevocationOrUsage()
    {
        var key = CreateValid();

        Assert.Null(key.RevokedAtUtc);
        Assert.Null(key.LastUsedAtUtc);
    }

    [Fact]
    public void Constructor_Throws_WhenIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            new ApiKey(Guid.Empty, Guid.NewGuid(), "n", new string('a', 64), "hk_live_", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Constructor_Throws_WhenTenantIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            new ApiKey(Guid.NewGuid(), Guid.Empty, "n", new string('a', 64), "hk_live_", DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenNameIsBlank(string name)
    {
        Assert.Throws<ArgumentException>(() =>
            new ApiKey(Guid.NewGuid(), Guid.NewGuid(), name, new string('a', 64), "hk_live_", DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenKeyHashIsBlank(string keyHash)
    {
        Assert.Throws<ArgumentException>(() =>
            new ApiKey(Guid.NewGuid(), Guid.NewGuid(), "n", keyHash, "hk_live_", DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenKeyPrefixIsBlank(string keyPrefix)
    {
        Assert.Throws<ArgumentException>(() =>
            new ApiKey(Guid.NewGuid(), Guid.NewGuid(), "n", new string('a', 64), keyPrefix, DateTimeOffset.UnixEpoch));
    }
}
