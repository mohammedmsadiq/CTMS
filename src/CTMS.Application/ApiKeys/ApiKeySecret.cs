using System.Security.Cryptography;
using System.Text;

namespace CTMS.Application.ApiKeys;

/// <summary>
/// Mints and hashes raw API keys. The raw key is <c>ctms_</c> followed by 40 URL-safe Base64
/// characters drawn from a CSPRNG (30 random bytes). Only the Base64 SHA-256 <see cref="Hash"/>
/// and the 8-character <see cref="Prefix"/> are ever persisted.
/// </summary>
public static class ApiKeySecret
{
    /// <summary>Literal prefix every raw key starts with.</summary>
    public const string RawKeyPrefix = "ctms_";

    private const int RandomByteCount = 30; // → 40 Base64 chars, no padding
    private const int DisplayPrefixLength = 8;

    /// <summary>A fresh raw key, e.g. <c>ctms_Qm8p...</c>. Return it to the caller once, then forget it.</summary>
    public static string NewRawKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(RandomByteCount);
        var body = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return RawKeyPrefix + body;
    }

    /// <summary>Base64 of the SHA-256 digest of the UTF-8 bytes of <paramref name="rawKey"/>.</summary>
    public static string Hash(string rawKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawKey);
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));
    }

    /// <summary>The display prefix stored alongside the hash — the first 8 characters of the raw key.</summary>
    public static string PrefixOf(string rawKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawKey);
        return rawKey.Length <= DisplayPrefixLength ? rawKey : rawKey[..DisplayPrefixLength];
    }
}
