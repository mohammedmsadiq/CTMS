namespace CTMS.Application.Translations.Import;

/// <summary>
/// Body for <c>POST /api/projects/{project}/import</c>.
/// <list type="bullet">
///   <item><see cref="Format"/> — <c>json</c>, <c>flat</c>, <c>csv</c> or <c>xlsx</c>;</item>
///   <item><see cref="Language"/> — a BCP-47 code enabled for the project. Required for the narrow
///   formats (<c>json</c>, <c>flat</c>, and a <c>csv</c>/<c>xlsx</c> file with a <c>value</c>
///   column); ignored for a wide <c>csv</c>/<c>xlsx</c> file (one that has language-code columns);</item>
///   <item><see cref="Content"/> — the raw file body for the text formats (<c>json</c>, <c>flat</c>,
///   <c>csv</c>);</item>
///   <item><see cref="ContentBase64"/> — the file bytes, base64-encoded, for <c>xlsx</c>;</item>
///   <item><see cref="Category"/> — applied to any key the import creates (a <c>category</c> column
///   in the file wins over this); when neither is present each new key's category is derived from
///   its name (see <see cref="CategorySuggestion"/>);</item>
///   <item><see cref="Status"/> — the review state of every upserted string: <c>Draft</c>
///   (default), <c>InReview</c> or <c>Approved</c>. <c>Published</c> and <c>Archived</c> are rejected;</item>
///   <item><see cref="DryRun"/> — when <c>true</c> the plan is computed but nothing is written.</item>
/// </list>
/// </summary>
public sealed record ImportTranslationsRequest(
    string Format,
    string? Language = null,
    string? Content = null,
    string? Category = null,
    string? Status = null,
    bool DryRun = false,
    string? ContentBase64 = null);

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
