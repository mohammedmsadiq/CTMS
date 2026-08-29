namespace CTMS.Application.Translations;

/// <summary>Read model returned by the translation-strings API.</summary>
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

/// <summary>
/// Payload for the string upsert. <see cref="ExpectedVersion"/>, when supplied, must match the
/// stored optimistic-concurrency token or the request is rejected with 409.
/// </summary>
public sealed record UpsertTranslationStringRequest(
    string Value,
    string? UpdatedBy = null,
    long? ExpectedVersion = null);

/// <summary>Outcome of an upsert: the resulting string plus whether a new row was created.</summary>
public sealed record UpsertTranslationStringResult(TranslationStringDto String, bool Created);

/// <summary>Payload for a review-workflow transition.</summary>
public sealed record ReviewRequest(string Action, string ReviewedBy);
