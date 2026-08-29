namespace CTMS.Application.Translations;

/// <summary>
/// Read model for a published translation bundle. <see cref="ETag"/> is the raw lowercase-hex
/// SHA-256 content hash; wrap it in double quotes to use it as an HTTP entity tag.
/// </summary>
public sealed record TranslationBundleDto(
    Guid Id,
    Guid ProjectId,
    string LocaleCode,
    int Version,
    IReadOnlyDictionary<string, string> Entries,
    string ETag,
    string CreatedBy,
    DateTime CreatedAt);

/// <summary>
/// Lightweight descriptor of one published bundle version, without the entries payload. Used by
/// the version-history listing.
/// </summary>
public sealed record BundleVersionDto(
    int Version,
    string ETag,
    DateTime CreatedAt,
    string CreatedBy,
    int EntryCount);

/// <summary>
/// Optional body for the publish endpoint. When <see cref="PublishedBy"/> is omitted the actor
/// recorded on the bundle and its audit entry falls back to <c>"system"</c>.
/// </summary>
public sealed record PublishBundleRequest(string? PublishedBy = null);
