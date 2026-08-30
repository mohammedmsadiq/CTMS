using CTMS.Application.Audit;
using CTMS.Application.Common;
using CTMS.Application.Languages;
using CTMS.Application.Projects;
using CTMS.Application.Translations;
using CTMS.Application.Translations.Import;
using CTMS.Infrastructure.Persistence.Caching;
using CTMS.Infrastructure.Persistence.Mongo;
using CTMS.Infrastructure.Persistence.Repositories;
using CTMS.Infrastructure.Persistence.Startup;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CTMS.Application.Tests.Infrastructure;

/// <summary>
/// One isolated database (with every production index applied) plus fully wired repositories
/// and services, for a single test. Dispose drops the database.
/// </summary>
public sealed class CtmsTestHarness : IDisposable
{
    private readonly IMongoClient _client;
    private readonly string _databaseName;

    public CtmsTestHarness(string connectionString)
    {
        _client = new MongoClient(connectionString);
        _databaseName = "ctms_test_" + Guid.NewGuid().ToString("N");

        Context = new CtmsMongoContext(_client, _databaseName);
        MongoIndexInitializer.EnsureIndexesAsync(Context).GetAwaiter().GetResult();

        Projects = new ProjectRepository(Context);
        Languages = new LanguageRepository(Context);
        Keys = new TranslationKeyRepository(Context);
        Strings = new TranslationStringRepository(Context);
        Audit = new AuditRepository(Context);

        DistributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        TranslationsCache = new PublishedTranslationsCache(
            DistributedCache,
            Options.Create(new TranslationsCacheOptions()),
            NullLogger<PublishedTranslationsCache>.Instance);

        var invalidator = new TranslationCacheInvalidator(Projects, TranslationsCache);

        ProjectService = new ProjectService(Projects, Languages, UnitOfWork);
        LanguageService = new LanguageService(Languages, UnitOfWork);
        TranslationKeyService = new TranslationKeyService(Keys, Projects, UnitOfWork);
        TranslationStringService = new TranslationStringService(
            Strings, Keys, Languages, Projects, Audit, invalidator, UnitOfWork);
        PublishedTranslationsService = new PublishedTranslationsService(
            Projects, Languages, Keys, Strings, Audit, TranslationsCache, invalidator, UnitOfWork);
        TranslationService = new TranslationService(PublishedTranslationsService);
        TranslationImportService = new TranslationImportService(
            Projects, Languages, Keys, Strings, Audit, invalidator, UnitOfWork);
        AuditService = new AuditService(Audit, Projects);
    }

    public IMongoContext Context { get; }

    public IUnitOfWork UnitOfWork { get; } = new NoOpUnitOfWork();

    public IProjectRepository Projects { get; }

    public ILanguageRepository Languages { get; }

    public ITranslationKeyRepository Keys { get; }

    public ITranslationStringRepository Strings { get; }

    public IAuditRepository Audit { get; }

    /// <summary>The <see cref="IDistributedCache"/> backing <see cref="TranslationsCache"/> — an
    /// in-memory stand-in for Redis. Exposed so tests can assert on raw cache entries.</summary>
    public IDistributedCache DistributedCache { get; }

    public IPublishedTranslationsCache TranslationsCache { get; }

    public ProjectService ProjectService { get; }

    public LanguageService LanguageService { get; }

    public TranslationKeyService TranslationKeyService { get; }

    public TranslationStringService TranslationStringService { get; }

    public PublishedTranslationsService PublishedTranslationsService { get; }

    public ITranslationService TranslationService { get; }

    public TranslationImportService TranslationImportService { get; }

    public AuditService AuditService { get; }

    public void Dispose() => _client.DropDatabase(_databaseName);
}
