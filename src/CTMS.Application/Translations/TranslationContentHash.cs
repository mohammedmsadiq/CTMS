using System.Security.Cryptography;
using System.Text;

namespace CTMS.Application.Translations;

/// <summary>
/// Stable content hash for an assembled translation map — the ETag for the client delivery
/// route. Lowercase-hex SHA-256 over the entries sorted by ordinal key, each emitted as
/// <c>key \n value \n</c> in UTF-8. Two assemblies with identical content produce byte-identical
/// hashes; any value change changes the hash. (This is the algorithm the old versioned bundle
/// used for its ETag.)
/// </summary>
public static class TranslationContentHash
{
    public static string Compute(IReadOnlyDictionary<string, string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var builder = new StringBuilder();
        foreach (var pair in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            builder.Append(pair.Key).Append('\n').Append(pair.Value).Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexStringLower(hash);
    }
}
