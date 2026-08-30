namespace CTMS.Application.Translations;

/// <summary>Read model returned by the translation-keys API.</summary>
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

/// <summary>
/// Payload for creating a translation key. <see cref="Category"/> is optional: when it is null
/// or blank the service derives one from the key-name prefix (see <see cref="CategorySuggestion"/>).
/// </summary>
public sealed record CreateTranslationKeyRequest(
    string KeyName,
    string? Category = null,
    string? Description = null,
    string? CreatedBy = null);

/// <summary>Partial update for a translation key; omitted members are left unchanged.</summary>
public sealed record UpdateTranslationKeyRequest(
    string? Category = null,
    string? Description = null,
    bool? Active = null);
