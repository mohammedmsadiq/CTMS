namespace CTMS.AdminUI.ApiContracts;

// These records mirror the wire contract of backend-core's /api/* endpoints
// (see docs/api.md). They are intentionally a copy — the UI never references
// CTMS.Application. Keep them in sync when the backend contract changes.
//
// Wire is camelCase (baseLanguageCode, translationKeyId, ...). The application
// model: applications identified by their `code` (slug), a global language
// catalogue, keys that carry a `category`, string `status` (no version token,
// last-write-wins), plus assemble-on-demand delivery.

// ---- Applications -------------------------------------------------------

public sealed record ApplicationDto(
    string Code,
    string Name,
    string? Description,
    bool IsShared,
    bool Active,
    string BaseLanguageCode,
    IReadOnlyList<string> EnabledLanguageCodes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateApplicationRequest(
    string Name,
    string BaseLanguageCode,
    string? Code = null,
    string? Description = null,
    bool IsShared = false,
    IReadOnlyList<string>? EnabledLanguageCodes = null);

public sealed record UpdateApplicationRequest(
    string? Name = null,
    string? Description = null,
    bool? IsShared = null,
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

// ---- Translation keys -----------------------------------------------------

public sealed record TranslationKeyDto(
    Guid Id,
    string Application,
    string KeyName,
    string Category,
    string? Description,
    bool Active,
    string CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateTranslationKeyRequest(
    string KeyName,
    string Category,
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

public sealed record TranslationValueDto(string Value, string Status);

public sealed record TranslationRowDto(
    Guid KeyId,
    string Key,
    string Category,
    string? Description,
    IReadOnlyDictionary<string, TranslationValueDto> Values);

// ---- Management: dashboard -----------------------------------------

public sealed record LanguageCoverageDto(
    string LanguageCode,
    string LanguageName,
    int TranslatedCount,
    int TotalKeys,
    double Percent,
    int MissingCount);

public sealed record DashboardResponse(
    int ApplicationCount,
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

public sealed record PublishTranslationsRequest(string Application, string? Language = null);

public sealed record PublishTranslationsResult(int Published);

// ---- Client delivery ------------------------------------------------

public sealed record PublishedTranslationsResponse(
    string Application,
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
    public const string NeedsReview = "NeedsReview";
    public const string Approved = "Approved";
    public const string Published = "Published";
}

public static class ReviewActions
{
    public const string Submit = "submit";
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Reopen = "reopen";
    public const string Publish = "publish";

    /// <summary>Legal review transitions for the string's current <c>status</c> (see docs/api.md).</summary>
    public static IReadOnlyList<(string Action, string Label)> ForState(string status) => status switch
    {
        ReviewStates.Draft => new[] { (Submit, "Submit for review") },
        ReviewStates.NeedsReview => new[] { (Approve, "Approve"), (Reject, "Reject") },
        ReviewStates.Approved => new[] { (Publish, "Publish"), (Reopen, "Reopen") },
        ReviewStates.Published => new[] { (Reopen, "Reopen") },
        _ => Array.Empty<(string, string)>(),
    };
}
