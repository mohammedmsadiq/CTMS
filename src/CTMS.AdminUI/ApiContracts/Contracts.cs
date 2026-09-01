namespace CTMS.AdminUI.ApiContracts;

// These records mirror the wire contract of backend-core's /api/* endpoints
// (see docs/api.md). They are intentionally a copy — the UI never references
// CTMS.Application. Keep them in sync when the backend contract changes.
//
// Wire is camelCase (baseLanguageCode, translationKeyId, ...). The application
// model: projects identified by their `code` (slug), a global language
// catalogue, keys that carry a `category`, string `status` (no version token,
// last-write-wins), plus assemble-on-demand delivery. The `common` project
// (isCommon: true) merges its published strings into every other project.

// ---- Projects -------------------------------------------------------

public sealed record ProjectDto(
    string Code,
    string Name,
    string? Description,
    bool IsCommon,
    bool Active,
    string BaseLanguageCode,
    IReadOnlyList<string> EnabledLanguageCodes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateProjectRequest(
    string Name,
    string BaseLanguageCode,
    string? Code = null,
    string? Description = null,
    bool IsCommon = false,
    IReadOnlyList<string>? EnabledLanguageCodes = null);

public sealed record UpdateProjectRequest(
    string? Name = null,
    string? Description = null,
    bool? IsCommon = null,
    bool? Active = null,
    string? BaseLanguageCode = null,
    IReadOnlyList<string>? EnabledLanguageCodes = null);

// ---- Languages (global) ----------------------------------------------

public sealed record LanguageDto(
    string Code,
    string Name,
    string? FallbackCode,
    bool IsRtl,
    bool Active,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateLanguageRequest(
    string Code,
    string Name,
    string? FallbackCode = null,
    bool IsRtl = false,
    bool Active = true);

public sealed record UpdateLanguageRequest(
    string? Name = null,
    string? FallbackCode = null,
    bool? IsRtl = null,
    bool? Active = null);

/// <summary>
/// A small client-side starter list of common BCP-47 codes for the new-project
/// wizard. The backend no longer exposes <c>GET /api/languages/suggestions</c>.
/// </summary>
public sealed record LanguageSuggestion(string Code, string Name, bool IsRtl);

public static class LanguageSuggestions
{
    public static readonly IReadOnlyList<LanguageSuggestion> Common = new[]
    {
        new LanguageSuggestion("en-GB", "English (United Kingdom)", false),
        new LanguageSuggestion("en-US", "English (United States)", false),
        new LanguageSuggestion("fr-FR", "French (France)", false),
        new LanguageSuggestion("de-DE", "German (Germany)", false),
        new LanguageSuggestion("es-ES", "Spanish (Spain)", false),
        new LanguageSuggestion("it-IT", "Italian (Italy)", false),
        new LanguageSuggestion("pt-PT", "Portuguese (Portugal)", false),
        new LanguageSuggestion("pt-BR", "Portuguese (Brazil)", false),
        new LanguageSuggestion("nl-NL", "Dutch (Netherlands)", false),
        new LanguageSuggestion("pl-PL", "Polish (Poland)", false),
        new LanguageSuggestion("sv-SE", "Swedish (Sweden)", false),
        new LanguageSuggestion("ja-JP", "Japanese (Japan)", false),
        new LanguageSuggestion("zh-CN", "Chinese (Simplified)", false),
        new LanguageSuggestion("ar-AE", "Arabic (United Arab Emirates)", true),
        new LanguageSuggestion("he-IL", "Hebrew (Israel)", true),
    };
}

// ---- Translation keys -----------------------------------------------------

public sealed record TranslationKeyDto(
    Guid Id,
    string Project,
    string KeyName,
    string Category,
    string? Description,
    bool Active,
    string CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateTranslationKeyRequest(
    string KeyName,
    string? Category = null,
    string? Description = null,
    string? CreatedBy = null);

public sealed record UpdateTranslationKeyRequest(
    string? Category = null,
    string? Description = null,
    bool? Active = null);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total);

// ---- Translation strings ------------------------------------------------

public sealed record TranslationStringDto(
    Guid Id,
    Guid TranslationKeyId,
    string LanguageCode,
    string Value,
    string Status,
    string? UpdatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Last write wins — there is no version token and no 409 on upsert.</summary>
public sealed record UpsertTranslationStringRequest(string Value, string? UpdatedBy = null);

// ---- Management: grid ------------------------------------------------

/// <summary>
/// One grid cell. <paramref name="Source"/> is <c>"app"</c> for a string that belongs to the
/// project in view, or <c>"shared:&lt;code&gt;"</c> when the value is borrowed from the Common
/// project's published set (read-only here — edit it on that project's grid).
/// </summary>
public sealed record TranslationValueDto(string Value, string Status, string? Source = null)
{
    /// <summary>The Common project code when <see cref="Source"/> is <c>shared:&lt;code&gt;</c>, else <c>null</c>.</summary>
    public string? BorrowedFrom =>
        Source is { } s && s.StartsWith("shared:", StringComparison.Ordinal) ? s["shared:".Length..] : null;

    public bool IsBorrowed => BorrowedFrom is not null;
}

public sealed record TranslationRowDto(
    Guid KeyId,
    string Key,
    string Category,
    string? Description,
    IReadOnlyDictionary<string, TranslationValueDto> Values,
    string? Source = null)
{
    /// <summary>The Common project code when this key is merged in from a common project, else <c>null</c>.</summary>
    public string? BorrowedFrom =>
        Source is { } s && s.StartsWith("shared:", StringComparison.Ordinal) ? s["shared:".Length..] : null;

    /// <summary>The key belongs to a common project, not the one in view — its metadata and values
    /// are read-only here and managed on that project's grid.</summary>
    public bool IsBorrowed => BorrowedFrom is not null;
}

// ---- Management: dashboard -----------------------------------------

public sealed record LanguageCoverageDto(
    string LanguageCode,
    string LanguageName,
    int TranslatedCount,
    int TotalKeys,
    double Percent,
    int MissingCount);

public sealed record DashboardResponse(
    int ProjectCount,
    int LanguageCount,
    int KeyCount,
    IReadOnlyList<LanguageCoverageDto> Coverage,
    int TotalMissing);

// ---- Management: missing -----------------------------------------

public sealed record MissingTranslationDto(
    Guid KeyId,
    string Key,
    string Category,
    IReadOnlyList<string> MissingLanguages);

// ---- Management: bulk publish ------------------------------------

public sealed record PublishTranslationsRequest(string Project, string? Language = null);

public sealed record PublishTranslationsResult(int Published);

// ---- Management: publish diff preview ------------------------------

/// <summary>One pending delivery change for a <c>(project, language)</c> pair.</summary>
public sealed record PublishPreviewChangeDto(
    string Key,
    string? CurrentValue,
    string NewValue,
    string Kind);

public sealed record PublishPreviewResult(
    string Project,
    string Language,
    IReadOnlyList<PublishPreviewChangeDto> Changes,
    int AddedCount,
    int ChangedCount);

// ---- Bulk language add ------------------------------------------

public sealed record BulkLanguageItem(
    string Code,
    string Name,
    string? FallbackCode = null,
    bool? IsRtl = null);

public sealed record BulkLanguagesRequest(IReadOnlyList<BulkLanguageItem> Languages);

public sealed record BulkLanguagesResult(
    IReadOnlyList<string> Created,
    IReadOnlyList<string> Skipped);

// ---- Import ------------------------------------------------------

/// <summary>
/// Body for <c>POST /api/projects/{project}/import</c>. <paramref name="Format"/> is one of
/// <c>json</c> / <c>flat</c>. <paramref name="DryRun"/> plans only.
/// </summary>
public sealed record ImportTranslationsRequest(
    string Format,
    string Language,
    string Content,
    string? Category = null,
    string? Status = null,
    bool DryRun = false);

public sealed record ImportErrorDto(int? Line, string? Key, string Message);

public sealed record ImportTranslationsResult(
    int CreatedKeys,
    int CreatedStrings,
    int UpdatedStrings,
    int Skipped,
    IReadOnlyList<ImportErrorDto> Errors,
    IReadOnlyList<string> Keys);

public static class ImportFormats
{
    public static readonly IReadOnlyList<(string Value, string Label)> All = new[]
    {
        ("json", "JSON (nested)"),
        ("flat", "Flat key=value"),
    };
}

// ---- Bulk review ------------------------------------------------

/// <summary>
/// Body for <c>POST /api/projects/{project}/review-bulk</c>. At least one of
/// <paramref name="Language"/> / <paramref name="Category"/> / <paramref name="KeyIds"/> must be
/// set or the server answers <c>400</c>. <paramref name="Action"/> is a <see cref="ReviewActions"/>
/// verb (<c>submit</c> / <c>approve</c> / <c>reject</c> / <c>reopen</c> / <c>publish</c> /
/// <c>archive</c> / <c>unarchive</c>).
/// </summary>
public sealed record ReviewBulkRequest(
    string Action,
    string? Language = null,
    string? Category = null,
    IReadOnlyList<Guid>? KeyIds = null,
    string? ReviewedBy = null);

public sealed record ReviewBulkResult(int Transitioned, int Skipped);

// ---- Client delivery ------------------------------------------------

public sealed record PublishedTranslationsResponse(
    string Project,
    string Language,
    IReadOnlyDictionary<string, string> Translations);

/// <summary>The delivery body plus the strong <c>ETag</c> validator from the response header.</summary>
public sealed record PublishedDelivery(PublishedTranslationsResponse Body, string? ETag);

// ---- History / audit trail -------------------------------------------

public sealed record AuditEntryDto(
    Guid Id,
    Guid ProjectId,
    string EntityType,
    Guid EntityId,
    string Action,
    string Actor,
    DateTime Timestamp,
    string? FromState,
    string? ToState,
    string? Detail,
    string? OldValue,
    string? NewValue);

// ---- Review workflow ----------------------------------------------------

public sealed record ReviewRequest(string Action, string ReviewedBy);

/// <summary>The review-state names as they appear on the wire (<c>status</c> / <c>fromState</c>).</summary>
public static class ReviewStates
{
    public const string Draft = "Draft";
    public const string InReview = "InReview";
    public const string Approved = "Approved";
    public const string Published = "Published";
    public const string Archived = "Archived";
}

public static class ReviewActions
{
    public const string Submit = "submit";
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Reopen = "reopen";
    public const string Publish = "publish";
    public const string Archive = "archive";
    public const string Unarchive = "unarchive";

    /// <summary>Legal review transitions for the string's current <c>status</c> (see docs/api.md).</summary>
    public static IReadOnlyList<(string Action, string Label)> ForState(string status) => status switch
    {
        ReviewStates.Draft => new[] { (Submit, "Submit for review"), (Archive, "Archive") },
        ReviewStates.InReview => new[] { (Approve, "Approve"), (Reject, "Reject"), (Archive, "Archive") },
        ReviewStates.Approved => new[] { (Publish, "Publish"), (Reopen, "Reopen"), (Archive, "Archive") },
        ReviewStates.Published => new[] { (Reopen, "Reopen"), (Archive, "Archive") },
        ReviewStates.Archived => new[] { (Unarchive, "Unarchive") },
        _ => Array.Empty<(string, string)>(),
    };
}
