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

    public async Task<IReadOnlyList<Project>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var filter = includeInactive
            ? FilterDefinition<Project>.Empty
            : Builders<Project>.Filter.Eq(p => p.Active, true);

        return await _projects.Find(filter).SortBy(p => p.Name).ToListAsync(cancellationToken);
    }

    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _projects.Find(p => p.Id == id).FirstOrDefaultAsync(cancellationToken);

    public async Task<Project?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
        => await _projects.Find(p => p.Slug == slug).FirstOrDefaultAsync(cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        => _projects.Find(p => p.Id == id).AnyAsync(cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default)
        => _projects.Find(p => p.Slug == slug).AnyAsync(cancellationToken);

    public async Task<IReadOnlyList<Project>> ListCommonAsync(CancellationToken cancellationToken = default)
        => await _projects.Find(p => p.IsCommon && p.Active)
            .SortBy(p => p.Name)
            .ToListAsync(cancellationToken);

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

    public async Task UpdateAsync(Project project, CancellationToken cancellationToken = default)
        => await _projects.ReplaceOneAsync(
            p => p.Id == project.Id,
            project.StampUpdated(),
            new ReplaceOptions(),
            cancellationToken);
}
