namespace CTMS.AdminUI.ApiContracts;

// These records mirror the wire contract of backend-core's /api/* endpoints
// (see docs/api.md). They are intentionally a copy — the UI never references
// CTMS.Application. Keep them in sync when the backend contract changes.

// ---- Projects -------------------------------------------------------------

public sealed record ProjectDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string BaseLocaleCode,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateProjectRequest(
    string Name,
    string BaseLocaleCode,
    string? Slug = null,
    string? Description = null);

// ---- Locales --------------------------------------------------------------

public sealed record LocaleDto(
    Guid Id,
    Guid ProjectId,
    string Code,
    string DisplayName,
    bool IsRtl,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateLocaleRequest(string Code, string DisplayName, bool IsRtl = false);

public sealed record UpdateLocaleRequest(string? DisplayName = null, bool? IsRtl = null);

// ---- Translation keys -----------------------------------------------------

public sealed record TranslationKeyDto(
    Guid Id,
    Guid ProjectId,
    string KeyName,
    string? Description,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateTranslationKeyRequest(string KeyName, string? Description = null);

public sealed record UpdateTranslationKeyRequest(string? Description = null);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total);

// ---- Translation strings ------------------------------------------------

public sealed record TranslationStringDto(
    Guid Id,
    Guid TranslationKeyId,
    Guid LocaleId,
    string LocaleCode,
    string Value,
    string ReviewState,
    string? UpdatedBy,
    long Version,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record UpsertTranslationStringRequest(
    string Value,
    string? UpdatedBy = null,
    long? ExpectedVersion = null);

// ---- Review workflow ----------------------------------------------------

public sealed record ReviewRequest(string Action, string ReviewedBy);

/// <summary>The review verbs the backend accepts, keyed by the state they act on.</summary>
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

    /// <summary>Legal transitions per current review state (see docs/api.md).</summary>
    public static IReadOnlyList<(string Action, string Label)> ForState(string reviewState) => reviewState switch
    {
        ReviewStates.Draft => new[] { (Submit, "Submit for review") },
        ReviewStates.NeedsReview => new[] { (Approve, "Approve"), (Reject, "Reject") },
        ReviewStates.Approved => new[] { (Publish, "Publish"), (Reopen, "Reopen") },
        ReviewStates.Published => new[] { (Reopen, "Reopen") },
        _ => Array.Empty<(string, string)>(),
    };
}
