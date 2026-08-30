namespace CTMS.Application.Translations.Import;

/// <summary>
/// Body for <c>POST /api/applications/{application}/import</c>.
/// <list type="bullet">
///   <item><see cref="Format"/> — <c>json</c> / <c>flat</c> / <c>csv</c> / <c>resx</c>;</item>
///   <item><see cref="Language"/> — a BCP-47 code that must be enabled for the application;</item>
///   <item><see cref="Content"/> — the raw file body;</item>
///   <item><see cref="Category"/> — applied to any key the import creates; when blank each new key's
///   category is derived from its name (see <see cref="CategorySuggestion"/>);</item>
///   <item><see cref="Status"/> — the review state of every upserted string:
///   <c>Draft</c> (default), <c>NeedsReview</c> or <c>Approved</c>. <c>Published</c> is rejected;</item>
///   <item><see cref="DryRun"/> — when <c>true</c> the plan is computed but nothing is written.</item>
/// </list>
/// </summary>
public sealed record ImportTranslationsRequest(
    string Format,
    string Language,
    string Content,
    string? Category = null,
    string? Status = null,
    bool DryRun = false);

/// <summary>One row the import could not apply (a bad key name, or a per-entry failure).</summary>
public sealed record ImportError(int? Line, string? Key, string Message);

/// <summary>Outcome of an import (or, for a dry run, the plan that would be applied).</summary>
public sealed record ImportTranslationsResult(
    int CreatedKeys,
    int CreatedStrings,
    int UpdatedStrings,
    int Skipped,
    IReadOnlyList<ImportError> Errors,
    IReadOnlyList<string> Keys);
