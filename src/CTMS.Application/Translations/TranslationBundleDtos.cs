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
