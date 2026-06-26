using System.Security.Cryptography;
using System.Text;

namespace Hiram.Infrastructure.Webhooks;

public static class WebhookSignature
{
    // sha256=<lowercase hex of HMAC-SHA256(body, secret)>, the convention tenants recompute to verify.
    public static string Compute(string secret, ReadOnlySpan<byte> body)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return "sha256=" + Convert.ToHexStringLower(hash);
    }
}
