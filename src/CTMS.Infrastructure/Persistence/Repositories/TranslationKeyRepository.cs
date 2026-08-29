using CTMS.Application.Common;
using CTMS.Application.Translations;
using CTMS.Domain.Translations;
using CTMS.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace CTMS.Infrastructure.Persistence.Repositories;

public sealed class TranslationKeyRepository : ITranslationKeyRepository
{
    private readonly IMongoContext _context;

    public TranslationKeyRepository(IMongoContext context) => _context = context;

    public async Task<IReadOnlyList<TranslationKey>> ListByProjectAsync(
        Guid projectId,
        string? category,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
        => await _context.TranslationKeys.Find(ByProjectAndCategory(projectId, category))
            .SortBy(k => k.KeyName)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(cancellationToken);

    public async Task<int> CountByProjectAsync(
        Guid projectId,
        string? category,
        CancellationToken cancellationToken = default)
        => (int)await _context.TranslationKeys.CountDocumentsAsync(
            ByProjectAndCategory(projectId, category),
            cancellationToken: cancellationToken);

    public async Task<TranslationKey?> GetAsync(Guid projectId, Guid keyId, CancellationToken cancellationToken = default)
        => await _context.TranslationKeys.Find(k => k.Id == keyId && k.ProjectId == projectId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> KeyNameExistsAsync(Guid projectId, string keyName, CancellationToken cancellationToken = default)
        => _context.TranslationKeys.Find(k => k.ProjectId == projectId && k.KeyName == keyName).AnyAsync(cancellationToken);

    public async Task<IReadOnlyList<TranslationKey>> ListByProjectsAsync(
        IReadOnlyCollection<Guid> projectIds,
        CancellationToken cancellationToken = default)
    {
        if (projectIds.Count == 0)
        {
            return [];
        }

        var filter = Builders<TranslationKey>.Filter.In(k => k.ProjectId, projectIds);
        return await _context.TranslationKeys.Find(filter).SortBy(k => k.KeyName).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(TranslationKey key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.TranslationKeys.InsertOneAsync(key.StampCreated(), cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.IsDuplicateKey())
        {
            throw new ConflictException($"A key named '{key.KeyName}' already exists in this application.");
        }
    }

    public async Task UpdateAsync(TranslationKey key, CancellationToken cancellationToken = default)
        => await _context.TranslationKeys.ReplaceOneAsync(
            k => k.Id == key.Id,
            key.StampUpdated(),
            new ReplaceOptions(),
            cancellationToken);

    public async Task RemoveAsync(TranslationKey key, CancellationToken cancellationToken = default)
    {
        // Explicitly remove the dependent strings; MongoDB does not enforce foreign keys.
        await _context.TranslationStrings.DeleteManyAsync(s => s.TranslationKeyId == key.Id, cancellationToken);
        await _context.TranslationKeys.DeleteOneAsync(k => k.Id == key.Id, cancellationToken);
    }

    private static FilterDefinition<TranslationKey> ByProjectAndCategory(Guid projectId, string? category)
    {
        var filter = Builders<TranslationKey>.Filter.Eq(k => k.ProjectId, projectId);
        if (!string.IsNullOrWhiteSpace(category))
        {
            filter &= Builders<TranslationKey>.Filter.Eq(k => k.Category, category.Trim());
        }

        return filter;
    }
}
