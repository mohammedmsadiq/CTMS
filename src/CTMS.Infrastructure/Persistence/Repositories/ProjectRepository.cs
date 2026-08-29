using CTMS.Application.Common;
using CTMS.Application.Projects;
using CTMS.Domain.Projects;
using CTMS.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace CTMS.Infrastructure.Persistence.Repositories;

public sealed class ProjectRepository : IProjectRepository
{
    private readonly IMongoCollection<Project> _projects;

    public ProjectRepository(IMongoContext context) => _projects = context.Projects;

    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default)
        => await _projects.Find(FilterDefinition<Project>.Empty)
            .SortBy(p => p.Name)
            .ToListAsync(cancellationToken);

    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _projects.Find(p => p.Id == id).FirstOrDefaultAsync(cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        => _projects.Find(p => p.Id == id).AnyAsync(cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default)
        => _projects.Find(p => p.Slug == slug).AnyAsync(cancellationToken);

    public async Task AddAsync(Project project, CancellationToken cancellationToken = default)
    {
        try
        {
            await _projects.InsertOneAsync(project.StampCreated(), cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.IsDuplicateKey())
        {
            throw new SlugAlreadyInUseException(project.Slug);
        }
    }
}
