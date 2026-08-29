using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CTMS.Client.Caching;

namespace CTMS.Client;

/// <summary>
/// Immutable client-side view of one published translation bundle: its entries plus the metadata
/// the SDK needs to revalidate it and to tell callers how fresh it is.
/// </summary>
public sealed class TranslationBundle
{
    internal TranslationBundle(
        Guid projectId,
        string localeCode,
        int version,
        IReadOnlyDictionary<string, string> entries,
        string etag,
        string? createdBy,
        DateTimeOffset? createdAt,
        DateTimeOffset retrievedAt,
        DateTimeOffset lastValidatedAt,
        bool isStale)
    {
        ProjectId = projectId;
        LocaleCode = localeCode;
        Version = version;
        Entries = entries;
        Etag = etag;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        RetrievedAt = retrievedAt;
        LastValidatedAt = lastValidatedAt;
        IsStale = isStale;
    }

    /// <summary>Project the bundle belongs to.</summary>
    public Guid ProjectId { get; }

    /// <summary>BCP-47 locale code exactly as the API returned it.</summary>
    public string LocaleCode { get; }

    /// <summary>Monotonic publish number for this <c>(project, locale)</c>, starting at 1.</summary>
    public int Version { get; }

    /// <summary>Key → value map. Keys are ordinal (case-sensitive), matching the server.</summary>
    public IReadOnlyDictionary<string, string> Entries { get; }

    /// <summary>Raw lowercase-hex SHA-256 content hash (unquoted).</summary>
    public string Etag { get; }

    /// <summary>Actor recorded on the published bundle, if the API supplied one.</summary>
    public string? CreatedBy { get; }

    /// <summary>Server publish timestamp, if the API supplied one.</summary>
    public DateTimeOffset? CreatedAt { get; }

    /// <summary>When the SDK last downloaded the bundle body.</summary>
    public DateTimeOffset RetrievedAt { get; }

    /// <summary>
    /// When the SDK last confirmed the bundle is current (a fresh <c>200</c> or a <c>304</c>).
    /// Equals <see cref="RetrievedAt"/> until the first successful revalidation.
    /// </summary>
    public DateTimeOffset LastValidatedAt { get; }

    /// <summary>
    /// <c>true</c> when this copy came from the cache after the API could not be reached, so it may
    /// be out of date. A successful fetch or revalidation always yields <c>false</c>.
    /// </summary>
    public bool IsStale { get; }

    /// <summary>Direct lookup in this bundle only (no fallback chain).</summary>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) => Entries.TryGetValue(key, out value!);

    internal static TranslationBundle FromStored(StoredBundle stored, bool isStale) => new(
        stored.ProjectId,
        stored.LocaleCode,
        stored.Version,
        stored.Entries,
        stored.Etag,
        stored.CreatedBy,
        stored.CreatedAt,
        stored.RetrievedAt,
        stored.LastValidatedAt,
        isStale);
}
