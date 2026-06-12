using Hiram.Application.Tenancy;

namespace Hiram.UnitTests.Tenancy;

public class ApiKeyHasherTests
{
    [Fact]
    public void Hash_IsDeterministic()
    {
        Assert.Equal(ApiKeyHasher.Hash("hk_live_abc"), ApiKeyHasher.Hash("hk_live_abc"));
    }

    [Fact]
    public void Hash_ProducesLowercaseHexOf64Characters()
    {
        var hash = ApiKeyHasher.Hash("hk_live_abc");

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void Hash_DiffersForDifferentInput()
    {
        Assert.NotEqual(ApiKeyHasher.Hash("hk_live_a"), ApiKeyHasher.Hash("hk_live_b"));
    }
}
