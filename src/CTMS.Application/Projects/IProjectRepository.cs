using CTMS.Domain.Projects;

namespace CTMS.Application.Projects;

/// <summary>Persistence operations for the <see cref="Project"/> (application) aggregate.</summary>
public interface IProjectRepository
{
    Task<IReadOnlyList<Project>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The application whose <see cref="Project.Slug"/> matches <paramref name="slug"/>, or <c>null</c>.</summary>
    Task<Project?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Every shared application (<see cref="Project.IsShared"/>), active only.</summary>
    Task<IReadOnlyList<Project>> ListSharedAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Project project, CancellationToken cancellationToken = default);

    Task UpdateAsync(Project project, CancellationToken cancellationToken = default);
}
