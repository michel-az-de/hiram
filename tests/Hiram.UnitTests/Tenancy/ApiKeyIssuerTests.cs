using Hiram.Application.Tenancy;

namespace Hiram.UnitTests.Tenancy;

public class ApiKeyIssuerTests
{
    [Fact]
    public void Issue_ProducesLiveKeyWithMatchingHashAndPrefix()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UnixEpoch;

        var issued = ApiKeyIssuer.Issue(tenantId, "easystok-server", now);

        Assert.StartsWith("hk_live_", issued.ClearKey);
        Assert.Equal(issued.ClearKey[..16], issued.ApiKey.KeyPrefix);
        // The prefix must carry secret characters past the "hk_live_" tag, otherwise keys are indistinguishable in listings.
        Assert.NotEqual("hk_live_", issued.ApiKey.KeyPrefix);
        Assert.Equal(ApiKeyHasher.Hash(issued.ClearKey), issued.ApiKey.KeyHash);
        Assert.Equal(tenantId, issued.ApiKey.TenantId);
        Assert.Equal("easystok-server", issued.ApiKey.Name);
        Assert.Equal(now, issued.ApiKey.CreatedAtUtc);
    }

    [Fact]
    public void Issue_ProducesUniqueSecretsAcrossCalls()
    {
        var a = ApiKeyIssuer.Issue(Guid.NewGuid(), "k", DateTimeOffset.UnixEpoch);
        var b = ApiKeyIssuer.Issue(Guid.NewGuid(), "k", DateTimeOffset.UnixEpoch);

        Assert.NotEqual(a.ClearKey, b.ClearKey);
        Assert.NotEqual(a.ApiKey.KeyHash, b.ApiKey.KeyHash);
        Assert.NotEqual(a.ApiKey.KeyPrefix, b.ApiKey.KeyPrefix);
    }
}
