using System.Security.Cryptography;
using System.Text;
using CTMS.Domain.Common;

namespace CTMS.Domain.Translations;

/// <summary>
/// An immutable, versioned snapshot of every published <see cref="TranslationString"/> for one
/// project and locale, taken at publish time. Bundles are append-only: a new publish produces a
/// new <see cref="Version"/> rather than mutating an existing row.
/// </summary>
public sealed class TranslationBundle : Entity
{
    private TranslationBundle()
    {
        // Materialization constructor for the persistence layer.
    }

    public TranslationBundle(
        Guid projectId,
        string localeCode,
        int version,
        IReadOnlyDictionary<string, string> entries,
        string createdBy)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("A bundle must belong to a project.", nameof(projectId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(localeCode);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);

        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Bundle versions start at 1.");
        }

        ProjectId = projectId;
        LocaleCode = localeCode.Trim();
        Version = version;
        Entries = new Dictionary<string, string>(entries, StringComparer.Ordinal);
        CreatedBy = createdBy.Trim();
        ETag = ComputeETag(Entries);
    }

    public Guid ProjectId { get; private set; }

    /// <summary>BCP-47 code of the locale this bundle was published for.</summary>
    public string LocaleCode { get; private set; } = string.Empty;

    /// <summary>Monotonic per <c>(ProjectId, LocaleCode)</c> publish number, starting at 1.</summary>
    public int Version { get; private set; }

    /// <summary>Immutable key→value map of every published string at publish time.</summary>
    public IReadOnlyDictionary<string, string> Entries { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string CreatedBy { get; private set; } = string.Empty;

    /// <summary>
    /// Stable content hash of the ordered entries: lowercase hex SHA-256 over the entries
    /// sorted by ordinal key, each emitted as <c>key \n value \n</c> in UTF-8. Callers that
    /// need an HTTP entity tag should wrap this in double quotes.
    /// </summary>
    public string ETag { get; private set; } = string.Empty;

    /// <summary>Computes the <see cref="ETag"/> for a set of entries. See that property for the format.</summary>
    public static string ComputeETag(IReadOnlyDictionary<string, string> entries)
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
