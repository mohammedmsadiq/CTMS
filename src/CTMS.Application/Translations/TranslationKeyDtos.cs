namespace CTMS.Application.Translations;

/// <summary>Read model returned by the translation-keys API.</summary>
public sealed record TranslationKeyDto(
    Guid Id,
    Guid ProjectId,
    string KeyName,
    string? Description,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Payload for creating a translation key.</summary>
public sealed record CreateTranslationKeyRequest(string KeyName, string? Description = null);

/// <summary>Partial update for a translation key.</summary>
public sealed record UpdateTranslationKeyRequest(string? Description = null);
