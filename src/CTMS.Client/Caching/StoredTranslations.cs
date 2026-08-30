using System;
using System.Collections.Generic;

namespace CTMS.Client.Caching;

/// <summary>
/// Serializable cache record for one <c>(application, language)</c> translation set. This is the
/// on-disk / in-memory shape; <see cref="TranslationSet"/> is the immutable view handed to callers.
/// There is no version field: the new delivery contract has no versioned bundles.
/// </summary>
public sealed class StoredTranslations
{
    /// <summary>Application code the set belongs to.</summary>
    public string Application { get; set; } = string.Empty;

    /// <summary>Language code exactly as the API returned it.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Flat key → value map, ordinal (case-sensitive) keys, matching the server.</summary>
    public Dictionary<string, string> Entries { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Raw lowercase-hex SHA-256 content hash (unquoted), from the response <c>ETag</c>.</summary>
    public string Etag { get; set; } = string.Empty;

    /// <summary>When the body was last downloaded.</summary>
    public DateTimeOffset RetrievedAt { get; set; }

    /// <summary>When the set was last confirmed current (a fresh <c>200</c> or a <c>304</c>).</summary>
    public DateTimeOffset LastValidatedAt { get; set; }

    internal StoredTranslations Clone() => new()
    {
        Application = Application,
        Language = Language,
        Entries = new Dictionary<string, string>(Entries, StringComparer.Ordinal),
        Etag = Etag,
        RetrievedAt = RetrievedAt,
        LastValidatedAt = LastValidatedAt,
    };
}
