namespace CTMS.Application.Languages;

/// <summary>Read model for a global language.</summary>
public sealed record LanguageDto(
    string Code,
    string Name,
    string? FallbackCode,
    bool IsRtl,
    bool Active,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Payload for registering a language.</summary>
public sealed record CreateLanguageRequest(
    string Code,
    string Name,
    string? FallbackCode = null,
    bool IsRtl = false,
    bool Active = true);

/// <summary>Partial update for a language; omitted members are left unchanged.</summary>
public sealed record UpdateLanguageRequest(
    string? Name = null,
    string? FallbackCode = null,
    bool? IsRtl = null,
    bool? Active = null);

/// <summary>One language to register in a <see cref="BulkCreateLanguagesRequest"/>.</summary>
public sealed record BulkCreateLanguageItem(
    string Code,
    string Name,
    string? FallbackCode = null,
    bool? IsRtl = null);

/// <summary>Body for <c>POST /api/languages/bulk</c>.</summary>
public sealed record BulkCreateLanguagesRequest(IReadOnlyList<BulkCreateLanguageItem> Languages);

/// <summary>
/// Result of a bulk language create: the codes that were newly registered and the codes that
/// already existed and were skipped (the call is idempotent).
/// </summary>
public sealed record BulkCreateLanguagesResult(
    IReadOnlyList<string> Created,
    IReadOnlyList<string> Skipped);
