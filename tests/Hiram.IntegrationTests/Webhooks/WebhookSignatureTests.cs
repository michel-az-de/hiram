using System.Security.Cryptography;
using System.Text;
using Hiram.Infrastructure.Webhooks;

namespace Hiram.IntegrationTests.Webhooks;

public class WebhookSignatureTests
{
    [Fact]
    public void Compute_MatchesIndependentHmac()
    {
        const string secret = "topsecret";
        var body = Encoding.UTF8.GetBytes("{\"a\":1}");
        var expected = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

        Assert.Equal(expected, WebhookSignature.Compute(secret, body));
    }

    [Fact]
    public void Compute_DiffersBySecret()
    {
        var body = Encoding.UTF8.GetBytes("payload");

        Assert.NotEqual(WebhookSignature.Compute("a", body), WebhookSignature.Compute("b", body));
    }
}
