using CTMS.Application.Common;
using CTMS.Application.Locales;
using CTMS.Domain.Locales;
using CTMS.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace CTMS.Infrastructure.Persistence.Repositories;

public sealed class LocaleRepository : ILocaleRepository
{
    private readonly IMongoContext _context;

    public LocaleRepository(IMongoContext context) => _context = context;

    public async Task<IReadOnlyList<Locale>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
        => await _context.Locales.Find(l => l.ProjectId == projectId)
            .SortBy(l => l.Code)
            .ToListAsync(cancellationToken);

    public async Task<Locale?> GetAsync(Guid projectId, Guid localeId, CancellationToken cancellationToken = default)
        => await _context.Locales.Find(l => l.Id == localeId && l.ProjectId == projectId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> CodeExistsAsync(Guid projectId, string code, CancellationToken cancellationToken = default)
        => _context.Locales.Find(l => l.ProjectId == projectId && l.Code == code).AnyAsync(cancellationToken);

    public async Task AddAsync(Locale locale, CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.Locales.InsertOneAsync(locale.StampCreated(), cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.IsDuplicateKey())
        {
            throw new ConflictException($"A locale with the code '{locale.Code}' already exists in this project.");
        }
    }

    public async Task UpdateAsync(Locale locale, CancellationToken cancellationToken = default)
        => await _context.Locales.ReplaceOneAsync(
            l => l.Id == locale.Id,
            locale.StampUpdated(),
            new ReplaceOptions(),
            cancellationToken);

    public async Task RemoveAsync(Locale locale, CancellationToken cancellationToken = default)
    {
        // Explicitly remove the dependent strings; MongoDB does not enforce foreign keys.
        await _context.TranslationStrings.DeleteManyAsync(s => s.LocaleId == locale.Id, cancellationToken);
        await _context.Locales.DeleteOneAsync(l => l.Id == locale.Id, cancellationToken);
    }
}
