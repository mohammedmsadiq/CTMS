namespace CTMS.Application.Translations;

/// <summary>
/// Client delivery payload: the flat <c>keyName → value</c> map assembled on demand for one
/// <c>(application, language)</c> pair. <see cref="Hash"/> is the ETag (not serialised in the
/// HTTP body — the endpoint puts it in the <c>ETag</c> header).
/// </summary>
public sealed record PublishedTranslationsView(
    string Application,
    string Language,
    IReadOnlyDictionary<string, string> Translations,
    string Hash);

/// <summary>HTTP body for <c>GET /api/translations/{application}/{language}</c>.</summary>
public sealed record PublishedTranslationsResponse(
    string Application,
    string Language,
    IReadOnlyDictionary<string, string> Translations);

/// <summary>One language cell in a management grid row.</summary>
public sealed record TranslationValueDto(string Value, string Status);

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
    int ApplicationCount,
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
public sealed record PublishTranslationsRequest(string Application, string? Language = null);

/// <summary>Result of a bulk publish.</summary>
public sealed record PublishTranslationsResult(int Published);
