using CTMS.Application.Translations;
using CTMS.Domain.Translations;
using Microsoft.EntityFrameworkCore;

namespace CTMS.Infrastructure.Persistence.Repositories;

public sealed class TranslationKeyRepository : ITranslationKeyRepository
{
    private readonly CtmsDbContext _db;

    public TranslationKeyRepository(CtmsDbContext db) => _db = db;

    public async Task<IReadOnlyList<TranslationKey>> ListByProjectAsync(
        Guid projectId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
        => await _db.TranslationKeys.AsNoTracking()
            .Where(k => k.ProjectId == projectId)
            .OrderBy(k => k.KeyName)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
        => _db.TranslationKeys.CountAsync(k => k.ProjectId == projectId, cancellationToken);

    public Task<TranslationKey?> GetAsync(Guid projectId, Guid keyId, CancellationToken cancellationToken = default)
        => _db.TranslationKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.ProjectId == projectId, cancellationToken);

    public Task<bool> KeyNameExistsAsync(Guid projectId, string keyName, CancellationToken cancellationToken = default)
        => _db.TranslationKeys.AnyAsync(k => k.ProjectId == projectId && k.KeyName == keyName, cancellationToken);

    public Task AddAsync(TranslationKey key, CancellationToken cancellationToken = default)
    {
        _db.TranslationKeys.Add(key);
        return Task.CompletedTask;
    }

    public async Task RemoveAsync(TranslationKey key, CancellationToken cancellationToken = default)
    {
        // The FK is configured OnDelete(DeleteBehavior.Cascade); delete the dependent strings
        // explicitly as well so the behaviour holds on providers that do not enforce FKs.
        var strings = await _db.TranslationStrings
            .Where(s => s.TranslationKeyId == key.Id)
            .ToListAsync(cancellationToken);

        _db.TranslationStrings.RemoveRange(strings);
        _db.TranslationKeys.Remove(key);
    }
}
