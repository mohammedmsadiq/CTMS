using CTMS.Application.Common;
using CTMS.Application.Translations;
using CTMS.Domain.Translations;
using CTMS.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace CTMS.Infrastructure.Persistence.Repositories;

// TODO: WS4 — the HTTP bundle endpoint (GET latest / by-version) and its Redis + ETag
// response caching wire onto this repository. This class only provides storage + retrieval.
public sealed class TranslationBundleRepository : ITranslationBundleRepository
{
    private readonly IMongoCollection<TranslationBundle> _bundles;

    public TranslationBundleRepository(IMongoContext context) => _bundles = context.TranslationBundles;

    public async Task<TranslationBundle?> GetLatestAsync(
        Guid projectId,
        string localeCode,
        CancellationToken cancellationToken = default)
        => await _bundles.Find(b => b.ProjectId == projectId && b.LocaleCode == localeCode)
            .SortByDescending(b => b.Version)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<TranslationBundle?> GetByVersionAsync(
        Guid projectId,
        string localeCode,
        int version,
        CancellationToken cancellationToken = default)
        => await _bundles.Find(b => b.ProjectId == projectId && b.LocaleCode == localeCode && b.Version == version)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<TranslationBundle>> ListByProjectAndLocaleAsync(
        Guid projectId,
        string localeCode,
        CancellationToken cancellationToken = default)
        => await _bundles.Find(b => b.ProjectId == projectId && b.LocaleCode == localeCode)
            .SortBy(b => b.Version)
            .ToListAsync(cancellationToken);

    public async Task InsertAsync(TranslationBundle bundle, CancellationToken cancellationToken = default)
    {
        try
        {
            await _bundles.InsertOneAsync(bundle.StampCreated(), cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.IsDuplicateKey())
        {
            throw new ConflictException(
                $"Bundle version {bundle.Version} for locale '{bundle.LocaleCode}' already exists in this project.");
        }
    }
}
