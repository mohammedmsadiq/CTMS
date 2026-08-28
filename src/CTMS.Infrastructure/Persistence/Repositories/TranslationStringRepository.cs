using CTMS.Application.Translations;
using CTMS.Domain.Translations;
using Microsoft.EntityFrameworkCore;

namespace CTMS.Infrastructure.Persistence.Repositories;

public sealed class TranslationStringRepository : ITranslationStringRepository
{
    private readonly CtmsDbContext _db;

    public TranslationStringRepository(CtmsDbContext db) => _db = db;

    public async Task<IReadOnlyList<TranslationString>> ListByKeyAsync(Guid keyId, CancellationToken cancellationToken = default)
        => await _db.TranslationStrings.AsNoTracking()
            .Where(s => s.TranslationKeyId == keyId)
            .ToListAsync(cancellationToken);

    public Task<TranslationString?> GetAsync(Guid keyId, Guid localeId, CancellationToken cancellationToken = default)
        => _db.TranslationStrings
            .FirstOrDefaultAsync(s => s.TranslationKeyId == keyId && s.LocaleId == localeId, cancellationToken);

    public Task AddAsync(TranslationString translationString, CancellationToken cancellationToken = default)
    {
        _db.TranslationStrings.Add(translationString);
        return Task.CompletedTask;
    }
}
