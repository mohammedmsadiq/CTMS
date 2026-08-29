using CTMS.Application.Common;
using CTMS.Application.Translations;
using CTMS.Domain.Translations;
using CTMS.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace CTMS.Infrastructure.Persistence.Repositories;

// Storage + retrieval only. The read-through cache and the ETag / If-None-Match / 304 handling
// on GET latest live in BundleCache (Persistence/Caching), TranslationBundleService, and
// BundleEndpoints respectively.
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
