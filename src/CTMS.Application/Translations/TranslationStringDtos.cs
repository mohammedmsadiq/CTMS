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
