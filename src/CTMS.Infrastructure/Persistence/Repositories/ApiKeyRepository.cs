using CTMS.Application.ApiKeys;
using CTMS.Application.Common;
using CTMS.Domain.ApiKeys;
using CTMS.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace CTMS.Infrastructure.Persistence.Repositories;

public sealed class ApiKeyRepository : IApiKeyRepository
{
    private readonly IMongoCollection<ApiKey> _apiKeys;

    public ApiKeyRepository(IMongoContext context) => _apiKeys = context.ApiKeys;

    public async Task<ApiKey?> FindByHashAsync(string hash, CancellationToken cancellationToken = default)
        => await _apiKeys.Find(k => k.Hash == hash).FirstOrDefaultAsync(cancellationToken);

    public async Task<ApiKey?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await _apiKeys.Find(k => k.Id == id).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ApiKey>> ListAsync(CancellationToken cancellationToken = default)
        => await _apiKeys.Find(FilterDefinition<ApiKey>.Empty)
            .SortByDescending(k => k.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task InsertAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await _apiKeys.InsertOneAsync(apiKey.StampCreated(), cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.IsDuplicateKey())
        {
            throw new ConflictException("An API key with the same hash already exists.");
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _apiKeys.DeleteOneAsync(k => k.Id == id, cancellationToken);
        return result.DeletedCount > 0;
    }

    public async Task TouchAsync(Guid id, DateTime whenUtc, CancellationToken cancellationToken = default)
    {
        var update = Builders<ApiKey>.Update
            .Set(k => k.LastUsedAt, whenUtc)
            .Set(k => k.UpdatedAt, whenUtc);
        await _apiKeys.UpdateOneAsync(k => k.Id == id, update, cancellationToken: cancellationToken);
    }
}
