using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CTMS.Client.Caching;

namespace CTMS.Client;

/// <summary>
/// Immutable client-side view of one assembled-on-demand translation set: its flat key → value
/// entries plus the metadata the SDK needs to revalidate it and to tell callers how fresh it is.
/// There is no version — the delivery contract identifies a set only by its content <see cref="Etag"/>.
/// </summary>
public sealed class TranslationSet
{
    internal TranslationSet(
        string application,
        string language,
        IReadOnlyDictionary<string, string> entries,
        string etag,
        DateTimeOffset retrievedAt,
        DateTimeOffset lastValidatedAt,
        bool isStale)
    {
        Application = application;
        Language = language;
        Entries = entries;
        Etag = etag;
        RetrievedAt = retrievedAt;
        LastValidatedAt = lastValidatedAt;
        IsStale = isStale;
    }

    /// <summary>Application code the set belongs to.</summary>
    public string Application { get; }

    /// <summary>BCP-47 language code exactly as the API returned it.</summary>
    public string Language { get; }

    /// <summary>Key → value map. Keys are ordinal (case-sensitive), matching the server.</summary>
    public IReadOnlyDictionary<string, string> Entries { get; }

    /// <summary>Raw lowercase-hex SHA-256 content hash (unquoted), from the response <c>ETag</c>.</summary>
    public string Etag { get; }

    /// <summary>When the SDK last downloaded the set body.</summary>
    public DateTimeOffset RetrievedAt { get; }

    /// <summary>
    /// When the SDK last confirmed the set is current (a fresh <c>200</c> or a <c>304</c>).
    /// Equals <see cref="RetrievedAt"/> until the first successful revalidation.
    /// </summary>
    public DateTimeOffset LastValidatedAt { get; }

    /// <summary>
    /// <c>true</c> when this copy came from the cache after the API could not be reached, so it may
    /// be out of date. A successful fetch or revalidation always yields <c>false</c>.
    /// </summary>
    public bool IsStale { get; }

    /// <summary>Direct lookup in this set only (no fallback chain).</summary>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) => Entries.TryGetValue(key, out value!);

    internal static TranslationSet FromStored(StoredTranslations stored, bool isStale) => new(
        stored.Application,
        stored.Language,
        new Dictionary<string, string>(stored.Entries, StringComparer.Ordinal),
        stored.Etag,
        stored.RetrievedAt,
        stored.LastValidatedAt,
        isStale);
}
