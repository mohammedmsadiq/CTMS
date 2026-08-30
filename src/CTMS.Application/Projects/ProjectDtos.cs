namespace CTMS.Application.Projects;

/// <summary>
/// Read model for a project. The <see cref="Code"/> (the slug) is the identifier used on the
/// client delivery routes.
/// </summary>
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

/// <summary>
/// Payload for creating a project. <see cref="Code"/> is derived from <see cref="Name"/>
/// when it is omitted.
/// </summary>
public sealed record CreateProjectRequest(
    string Name,
    string BaseLanguageCode,
    string? Code = null,
    string? Description = null,
    bool IsCommon = false,
    IReadOnlyList<string>? EnabledLanguageCodes = null);

/// <summary>Partial update for a project; omitted members are left unchanged.</summary>
public sealed record UpdateProjectRequest(
    string? Name = null,
    string? Description = null,
    bool? IsCommon = null,
    bool? Active = null,
    string? BaseLanguageCode = null,
    IReadOnlyList<string>? EnabledLanguageCodes = null);
