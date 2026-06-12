using System.Security.Cryptography;
using System.Text;

namespace Hiram.Application.Tenancy;

public static class ApiKeyHasher
{
    // Keys carry 256 bits of entropy, so a plain SHA-256 is enough to make the stored value useless
    // to an attacker while keeping the auth lookup a single indexed equality on the hash.
    public static string Hash(string key)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexStringLower(digest);
    }
}
