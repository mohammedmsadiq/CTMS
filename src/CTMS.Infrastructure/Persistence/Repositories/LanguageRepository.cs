using CTMS.Application.Common;
using CTMS.Application.Languages;
using CTMS.Domain.Languages;
using CTMS.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace CTMS.Infrastructure.Persistence.Repositories;

public sealed class LanguageRepository : ILanguageRepository
{
    private readonly IMongoCollection<Language> _languages;

    public LanguageRepository(IMongoContext context) => _languages = context.Languages;

    public async Task<IReadOnlyList<Language>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var filter = includeInactive
            ? FilterDefinition<Language>.Empty
            : Builders<Language>.Filter.Eq(l => l.Active, true);

        return await _languages.Find(filter).SortBy(l => l.Code).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Language>> ListAllAsync(CancellationToken cancellationToken = default)
        => await _languages.Find(FilterDefinition<Language>.Empty)
            .SortBy(l => l.Code)
            .ToListAsync(cancellationToken);

    public async Task<Language?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
        => await _languages.Find(l => l.Code == code).FirstOrDefaultAsync(cancellationToken);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default)
        => _languages.Find(l => l.Code == code).AnyAsync(cancellationToken);

    public async Task AddAsync(Language language, CancellationToken cancellationToken = default)
    {
        try
        {
            await _languages.InsertOneAsync(language.StampCreated(), cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.IsDuplicateKey())
        {
            throw new ConflictException($"A language with the code '{language.Code}' already exists.");
        }
    }

    public async Task UpdateAsync(Language language, CancellationToken cancellationToken = default)
        => await _languages.ReplaceOneAsync(
            l => l.Id == language.Id,
            language.StampUpdated(),
            new ReplaceOptions(),
            cancellationToken);
}
