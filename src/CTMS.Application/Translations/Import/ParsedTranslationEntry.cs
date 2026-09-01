namespace CTMS.Application.Translations.Import;

/// <summary>
/// One upsert a translation-file parser produced.
/// <list type="bullet">
///   <item><see cref="Key"/> / <see cref="Value"/> — the pair to upsert;</item>
///   <item><see cref="LanguageCode"/> — the target language for a <em>wide</em> file (one entry per
///   <c>(key, language column)</c>); <c>null</c> for a narrow file, where the request's
///   <c>language</c> applies;</item>
///   <item><see cref="Category"/> / <see cref="Description"/> — from the optional <c>category</c> /
///   <c>description</c> columns; applied only when the import creates the key;</item>
///   <item><see cref="Line"/> — 1-based source line / row for error reporting, when known.</item>
/// </list>
/// </summary>
public sealed record ParsedTranslationEntry(
    string Key,
    string Value,
    string? LanguageCode = null,
    string? Category = null,
    string? Description = null,
    int? Line = null);

/// <summary>
/// The result of parsing a translation file: the ordered <see cref="Entries"/> plus whether the
/// file was <see cref="IsWide"/> (a <c>key</c> column plus one or more language-code columns) — a
/// narrow file still needs the request's <c>language</c>, a wide one ignores it.
/// </summary>
public sealed record ParsedTranslationFile(bool IsWide, IReadOnlyList<ParsedTranslationEntry> Entries);
