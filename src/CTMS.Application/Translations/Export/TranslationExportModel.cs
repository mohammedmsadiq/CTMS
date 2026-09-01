namespace CTMS.Application.Translations.Export;

/// <summary>
/// One key's row in an export: its metadata plus the current value per language column
/// (<c>null</c> / absent when the key has no string in that language).
/// </summary>
public sealed record TranslationExportRow(
    string Key,
    string Category,
    string? Description,
    IReadOnlyDictionary<string, string?> Values);

/// <summary>
/// The shape <see cref="TranslationExporter"/> renders: the project slug, the ordered language
/// columns and the ordered key rows. Assembled by <see cref="TranslationExportService"/> so the
/// writers stay pure and unit-testable without a store or HTTP.
/// </summary>
public sealed record TranslationExportData(
    string ProjectSlug,
    IReadOnlyList<string> LanguageCodes,
    IReadOnlyList<TranslationExportRow> Rows);
