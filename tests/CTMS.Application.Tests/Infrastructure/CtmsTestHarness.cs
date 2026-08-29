using CTMS.Application.Audit;
using CTMS.Application.Common;
using CTMS.Application.Locales;
using CTMS.Application.Projects;
using CTMS.Application.Translations;
using CTMS.Infrastructure.Persistence.Mongo;
using CTMS.Infrastructure.Persistence.Repositories;
using CTMS.Infrastructure.Persistence.Startup;
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
        Locales = new LocaleRepository(Context);
        Keys = new TranslationKeyRepository(Context);
        Strings = new TranslationStringRepository(Context);
        Bundles = new TranslationBundleRepository(Context);
        Audit = new AuditRepository(Context);

        ProjectService = new ProjectService(Projects, UnitOfWork);
        LocaleService = new LocaleService(Locales, Projects, UnitOfWork);
        TranslationKeyService = new TranslationKeyService(Keys, Projects, UnitOfWork);
        TranslationStringService = new TranslationStringService(Strings, Keys, Locales, Projects, Audit, UnitOfWork);
        TranslationBundleService = new TranslationBundleService(Bundles, Strings, Keys, Locales, Projects, Audit, UnitOfWork);
        AuditService = new AuditService(Audit);
    }

    public IMongoContext Context { get; }

    public IUnitOfWork UnitOfWork { get; } = new NoOpUnitOfWork();

    public IProjectRepository Projects { get; }

    public ILocaleRepository Locales { get; }

    public ITranslationKeyRepository Keys { get; }

    public ITranslationStringRepository Strings { get; }

    public ITranslationBundleRepository Bundles { get; }

    public IAuditRepository Audit { get; }

    public ProjectService ProjectService { get; }

    public LocaleService LocaleService { get; }

    public TranslationKeyService TranslationKeyService { get; }

    public TranslationStringService TranslationStringService { get; }

    public TranslationBundleService TranslationBundleService { get; }

    public AuditService AuditService { get; }

    public void Dispose() => _client.DropDatabase(_databaseName);
}
