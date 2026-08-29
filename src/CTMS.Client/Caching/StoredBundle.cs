using System;
using System.Collections.Generic;

namespace CTMS.Client.Caching;

/// <summary>
/// Serializable cache record for one bundle. This is the on-disk / in-memory shape;
/// <see cref="TranslationBundle"/> is the immutable view handed to callers.
/// </summary>
public sealed class StoredBundle
{
    public Guid ProjectId { get; set; }

    public string LocaleCode { get; set; } = string.Empty;

    public int Version { get; set; }

    public Dictionary<string, string> Entries { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Raw lowercase-hex SHA-256 content hash (unquoted).</summary>
    public string Etag { get; set; } = string.Empty;

    public string? CreatedBy { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>When the body was last downloaded.</summary>
    public DateTimeOffset RetrievedAt { get; set; }

    /// <summary>When the bundle was last confirmed current (fresh <c>200</c> or <c>304</c>).</summary>
    public DateTimeOffset LastValidatedAt { get; set; }

    internal StoredBundle Clone() => new()
    {
        ProjectId = ProjectId,
        LocaleCode = LocaleCode,
        Version = Version,
        Entries = new Dictionary<string, string>(Entries, StringComparer.Ordinal),
        Etag = Etag,
        CreatedBy = CreatedBy,
        CreatedAt = CreatedAt,
        RetrievedAt = RetrievedAt,
        LastValidatedAt = LastValidatedAt,
    };
}
