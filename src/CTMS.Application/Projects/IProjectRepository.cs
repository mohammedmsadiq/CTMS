using CTMS.Domain.Projects;

namespace CTMS.Application.Projects;

/// <summary>Persistence operations for the <see cref="Project"/> aggregate.</summary>
public interface IProjectRepository
{
    Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default);

    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);

    Task AddAsync(Project project, CancellationToken cancellationToken = default);
}
