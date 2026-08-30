namespace CTMS.Application.Translations;

/// <summary>
/// Client delivery payload: the flat <c>keyName → value</c> map assembled on demand for one
/// <c>(project, language)</c> pair. <see cref="Hash"/> is the ETag (not serialised in the
/// HTTP body — the endpoint puts it in the <c>ETag</c> header).
/// </summary>
public sealed record PublishedTranslationsView(
    string Project,
    string Language,
    IReadOnlyDictionary<string, string> Translations,
    string Hash);

/// <summary>HTTP body for <c>GET /api/translations/{project}/{language}</c>.</summary>
public sealed record PublishedTranslationsResponse(
    string Project,
    string Language,
    IReadOnlyDictionary<string, string> Translations);

/// <summary>
/// One language cell in a management grid row. <see cref="Source"/> is <c>app</c> when the value
/// is the project's own string, or <c>shared:&lt;code&gt;</c> when it comes from a common
/// project whose keys are merged into the grid.
/// </summary>
public sealed record TranslationValueDto(string Value, string Status, string Source);

/// <summary>One management grid row — a key and its value per enabled language.</summary>
public sealed record TranslationRowDto(
    Guid KeyId,
    string Key,
    string Category,
    string? Description,
    IReadOnlyDictionary<string, TranslationValueDto> Values);

/// <summary>Per-language coverage for the dashboard.</summary>
public sealed record LanguageCoverageDto(
    string LanguageCode,
    string LanguageName,
    int TranslatedCount,
    int TotalKeys,
    double Percent,
    int MissingCount);

/// <summary>Response for <c>GET /api/dashboard</c>.</summary>
public sealed record DashboardResponse(
    int ProjectCount,
    int LanguageCount,
    int KeyCount,
    IReadOnlyList<LanguageCoverageDto> Coverage,
    int TotalMissing);

/// <summary>One row for <c>GET /api/translations/missing</c>.</summary>
public sealed record MissingTranslationDto(
    Guid KeyId,
    string Key,
    string Category,
    IReadOnlyList<string> MissingLanguages);

/// <summary>Body for <c>POST /api/translations/publish</c>.</summary>
public sealed record PublishTranslationsRequest(string Project, string? Language = null);

/// <summary>Result of a bulk publish.</summary>
public sealed record PublishTranslationsResult(int Published);

/// <summary>
/// One entry in a publish preview: what publishing the project's <c>Approved</c> strings for a
/// language would change in the delivered map. <see cref="Kind"/> is <c>added</c> (the key is not
/// currently delivered) or <c>changed</c> (a delivered value would differ).
/// </summary>
public sealed record PublishPreviewChange(
    string Key,
    string? CurrentValue,
    string NewValue,
    string Kind);

/// <summary>Response for <c>GET /api/translations/publish/preview</c>.</summary>
public sealed record PublishPreviewResponse(
    string Project,
    string Language,
    IReadOnlyList<PublishPreviewChange> Changes,
    int AddedCount,
    int ChangedCount);
