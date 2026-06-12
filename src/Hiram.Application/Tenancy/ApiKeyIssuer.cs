using System.Numerics;
using System.Security.Cryptography;
using Hiram.Domain.Tenants;

namespace Hiram.Application.Tenancy;

public static class ApiKeyIssuer
{
    private const string LivePrefix = "hk_live_";
    private const string Base62Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static IssuedApiKey Issue(Guid tenantId, string name, DateTimeOffset now)
    {
        Span<byte> secret = stackalloc byte[32];
        RandomNumberGenerator.Fill(secret);

        var clearKey = LivePrefix + ToBase62(secret);
        var apiKey = new ApiKey(
            Guid.NewGuid(),
            tenantId,
            name,
            ApiKeyHasher.Hash(clearKey),
            clearKey[..8],
            now);

        return new IssuedApiKey(apiKey, clearKey);
    }

    private static string ToBase62(ReadOnlySpan<byte> bytes)
    {
        var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        if (value.IsZero)
            return "0";

        var digits = new Stack<char>();
        while (value > 0)
        {
            value = BigInteger.DivRem(value, 62, out var remainder);
            digits.Push(Base62Alphabet[(int)remainder]);
        }

        return new string(digits.ToArray());
    }
}
