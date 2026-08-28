namespace CTMS.Application.Projects;

/// <summary>Read model returned by the projects API.</summary>
public sealed record ProjectDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string BaseLocaleCode,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
/// Payload for creating a project. <see cref="Slug"/> is derived from <see cref="Name"/>
/// when it is omitted.
/// </summary>
public sealed record CreateProjectRequest(
    string Name,
    string BaseLocaleCode,
    string? Slug = null,
    string? Description = null);
