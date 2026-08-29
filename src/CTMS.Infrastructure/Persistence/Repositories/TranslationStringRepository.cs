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
        => await _strings.Find(s => s.TranslationKeyId == keyId).ToListAsync(cancellationToken);

    public async Task<TranslationString?> GetAsync(Guid keyId, Guid localeId, CancellationToken cancellationToken = default)
        => await _strings.Find(s => s.TranslationKeyId == keyId && s.LocaleId == localeId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<TranslationString>> ListByLocaleAndStateAsync(
        Guid localeId,
        ReviewState state,
        CancellationToken cancellationToken = default)
        => await _strings.Find(s => s.LocaleId == localeId && s.ReviewState == state).ToListAsync(cancellationToken);

    public async Task AddAsync(TranslationString translationString, CancellationToken cancellationToken = default)
    {
        translationString.Version = 0;
        try
        {
            await _strings.InsertOneAsync(translationString.StampCreated(), cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.IsDuplicateKey())
        {
            throw new ConflictException(
                $"A translation string for key '{translationString.TranslationKeyId}' and locale " +
                $"'{translationString.LocaleId}' already exists.");
        }
    }

    public async Task UpdateAsync(TranslationString translationString, CancellationToken cancellationToken = default)
    {
        var expected = translationString.Version;
        translationString.StampUpdated();
        translationString.Version = expected + 1;

        var filter = Builders<TranslationString>.Filter.And(
            Builders<TranslationString>.Filter.Eq(s => s.Id, translationString.Id),
            Builders<TranslationString>.Filter.Eq(s => s.Version, expected));

        var result = await _strings.ReplaceOneAsync(filter, translationString, new ReplaceOptions(), cancellationToken);

        if (result.IsAcknowledged && result.MatchedCount == 0)
        {
            var current = await _strings.Find(s => s.Id == translationString.Id).FirstOrDefaultAsync(cancellationToken);
            throw new ConcurrencyException(current?.Version ?? expected);
        }
    }
}
