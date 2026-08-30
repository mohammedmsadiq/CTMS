using CTMS.Application.Common;
using CTMS.Application.Translations;
using CTMS.Domain.Translations;
using CTMS.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace CTMS.Infrastructure.Persistence.Repositories;

public sealed class TranslationStringRepository : ITranslationStringRepository
{
    private readonly IMongoCollection<TranslationString> _strings;

    public TranslationStringRepository(IMongoContext context) => _strings = context.TranslationStrings;

    public async Task<IReadOnlyList<TranslationString>> ListByKeyAsync(Guid keyId, CancellationToken cancellationToken = default)
        => await _strings.Find(s => s.TranslationKeyId == keyId)
            .SortBy(s => s.LanguageCode)
            .ToListAsync(cancellationToken);

    public async Task<TranslationString?> GetAsync(Guid keyId, string languageCode, CancellationToken cancellationToken = default)
        => await _strings.Find(s => s.TranslationKeyId == keyId && s.LanguageCode == languageCode)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<TranslationString>> ListByKeyIdsAsync(
        IReadOnlyCollection<Guid> keyIds,
        CancellationToken cancellationToken = default)
    {
        if (keyIds.Count == 0)
        {
            return [];
        }

        var filter = Builders<TranslationString>.Filter.In(s => s.TranslationKeyId, keyIds);
        return await _strings.Find(filter).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TranslationString>> ListPublishedByKeyIdsAsync(
        IReadOnlyCollection<Guid> keyIds,
        CancellationToken cancellationToken = default)
    {
        if (keyIds.Count == 0)
        {
            return [];
        }

        var builder = Builders<TranslationString>.Filter;
        var filter = builder.In(s => s.TranslationKeyId, keyIds)
            & builder.Eq(s => s.ReviewState, ReviewState.Published);
        return await _strings.Find(filter).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TranslationString>> ListApprovedByKeyIdsAsync(
        IReadOnlyCollection<Guid> keyIds,
        string? languageCode,
        CancellationToken cancellationToken = default)
    {
        if (keyIds.Count == 0)
        {
            return [];
        }

        var builder = Builders<TranslationString>.Filter;
        var filter = builder.In(s => s.TranslationKeyId, keyIds)
            & builder.Eq(s => s.ReviewState, ReviewState.Approved);
        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            filter &= builder.Eq(s => s.LanguageCode, languageCode.Trim());
        }

        return await _strings.Find(filter).ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<TranslationString>> ListByKeysAndStateAsync(
        IReadOnlyCollection<Guid> keyIds,
        ReviewState? state,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var builder = Builders<TranslationString>.Filter;
        var filter = builder.In(s => s.TranslationKeyId, keyIds);
        if (state is { } wanted)
        {
            filter &= builder.Eq(s => s.ReviewState, wanted);
        }

        var total = (int)await _strings.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

        var items = await _strings.Find(filter)
            .SortByDescending(s => s.UpdatedAt)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(cancellationToken);

        return new PagedResult<TranslationString>(items, total);
    }

    public async Task AddAsync(TranslationString translationString, CancellationToken cancellationToken = default)
    {
        try
        {
            await _strings.InsertOneAsync(translationString.StampCreated(), cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.IsDuplicateKey())
        {
            throw new ConflictException(
                $"A translation string for key '{translationString.TranslationKeyId}' and language " +
                $"'{translationString.LanguageCode}' already exists.");
        }
    }

    public async Task UpdateAsync(TranslationString translationString, CancellationToken cancellationToken = default)
        => await _strings.ReplaceOneAsync(
            s => s.Id == translationString.Id,
            translationString.StampUpdated(),
            new ReplaceOptions(),
            cancellationToken);
}
