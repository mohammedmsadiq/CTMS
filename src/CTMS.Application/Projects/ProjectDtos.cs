namespace CTMS.Application.Projects;

/// <summary>
/// Read model for an application. The <see cref="Code"/> (the slug) is the identifier used on
/// the client delivery routes.
/// </summary>
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

/// <summary>
/// Payload for creating an application. <see cref="Code"/> is derived from <see cref="Name"/>
/// when it is omitted.
/// </summary>
public sealed record CreateApplicationRequest(
    string Name,
    string BaseLanguageCode,
    string? Code = null,
    string? Description = null,
    bool IsShared = false,
    IReadOnlyList<string>? EnabledLanguageCodes = null);

/// <summary>Partial update for an application; omitted members are left unchanged.</summary>
public sealed record UpdateApplicationRequest(
    string? Name = null,
    string? Description = null,
    bool? IsShared = null,
    bool? Active = null,
    string? BaseLanguageCode = null,
    IReadOnlyList<string>? EnabledLanguageCodes = null);
