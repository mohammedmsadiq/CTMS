using CTMS.Application.Projects;
using CTMS.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace CTMS.Infrastructure.Persistence.Repositories;

public sealed class ProjectRepository : IProjectRepository
{
    private readonly CtmsDbContext _db;

    public ProjectRepository(CtmsDbContext db) => _db = db;

    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default)
        => await _db.Projects.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default)
        => _db.Projects.AnyAsync(p => p.Slug == slug, cancellationToken);

    public Task AddAsync(Project project, CancellationToken cancellationToken = default)
    {
        _db.Projects.Add(project);
        return Task.CompletedTask;
    }
}
