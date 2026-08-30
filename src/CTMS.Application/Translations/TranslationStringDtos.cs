namespace CTMS.Application.Translations;

/// <summary>Read model returned by the translation-strings API.</summary>
public sealed record TranslationStringDto(
    Guid Id,
    Guid TranslationKeyId,
    string LanguageCode,
    string Value,
    string Status,
    string? UpdatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Payload for the string upsert. Last write wins — there is no version token.</summary>
public sealed record UpsertTranslationStringRequest(
    string Value,
    string? UpdatedBy = null);

/// <summary>Outcome of an upsert: the resulting string plus whether a new row was created.</summary>
public sealed record UpsertTranslationStringResult(TranslationStringDto String, bool Created);

/// <summary>Payload for a review-workflow transition.</summary>
public sealed record ReviewRequest(string Action, string ReviewedBy);

/// <summary>
/// Body for <c>POST /api/projects/{project}/review-bulk</c>. The <see cref="Action"/> is
/// applied to every string of the project that matches the optional <see cref="Language"/> /
/// <see cref="Category"/> / <see cref="KeyIds"/> filters and is in a state the action is legal
/// from; illegal ones are skipped, not errored. At least one filter is required.
/// </summary>
public sealed record ReviewBulkRequest(
    string Action,
    string? Language = null,
    string? Category = null,
    IReadOnlyList<Guid>? KeyIds = null,
    string? ReviewedBy = null);

/// <summary>Result of a bulk review: how many strings transitioned and how many were skipped.</summary>
public sealed record ReviewBulkResult(int Transitioned, int Skipped);
