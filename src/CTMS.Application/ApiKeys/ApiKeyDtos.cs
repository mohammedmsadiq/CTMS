namespace CTMS.Application.ApiKeys;

/// <summary>Body for <c>POST /api/api-keys</c>.</summary>
public sealed record CreateApiKeyRequest(string Name);

/// <summary>
/// Read model for a listed key. Carries no secret material — not the hash, not the raw key.
/// </summary>
public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    string Prefix,
    string CreatedBy,
    bool Active,
    DateTime? LastUsedAt,
    DateTime CreatedAt);

/// <summary>
/// Response for <c>POST /api/api-keys</c> — the same fields as <see cref="ApiKeyDto"/> plus the
/// one and only disclosure of the raw <see cref="Key"/>. Store it now; it cannot be retrieved later.
/// </summary>
public sealed record CreatedApiKeyDto(
    Guid Id,
    string Name,
    string Prefix,
    string CreatedBy,
    bool Active,
    DateTime CreatedAt,
    string Key);
