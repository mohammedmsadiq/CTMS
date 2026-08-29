using CTMS.Domain.Audit;
using CTMS.Domain.Locales;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;
using MongoDB.Driver;

namespace CTMS.Infrastructure.Persistence.Mongo;

/// <summary>
/// Wraps an <see cref="IMongoClient"/> and its database, exposing one typed collection per
/// aggregate. Registered as a singleton; the driver's collection handles are thread-safe.
/// </summary>
public sealed class CtmsMongoContext : IMongoContext
{
    public const string ProjectsCollection = "projects";
    public const string LocalesCollection = "locales";
    public const string TranslationKeysCollection = "translationKeys";
    public const string TranslationStringsCollection = "translationStrings";
    public const string TranslationBundlesCollection = "translationBundles";
    public const string AuditEntriesCollection = "auditEntries";

    public CtmsMongoContext(IMongoClient client, string databaseName)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        MongoMappingRegistration.Register();

        Database = client.GetDatabase(databaseName);
        Projects = Database.GetCollection<Project>(ProjectsCollection);
        Locales = Database.GetCollection<Locale>(LocalesCollection);
        TranslationKeys = Database.GetCollection<TranslationKey>(TranslationKeysCollection);
        TranslationStrings = Database.GetCollection<TranslationString>(TranslationStringsCollection);
        TranslationBundles = Database.GetCollection<TranslationBundle>(TranslationBundlesCollection);
        AuditEntries = Database.GetCollection<AuditEntry>(AuditEntriesCollection);
    }

    public IMongoDatabase Database { get; }

    public IMongoCollection<Project> Projects { get; }

    public IMongoCollection<Locale> Locales { get; }

    public IMongoCollection<TranslationKey> TranslationKeys { get; }

    public IMongoCollection<TranslationString> TranslationStrings { get; }

    public IMongoCollection<TranslationBundle> TranslationBundles { get; }

    public IMongoCollection<AuditEntry> AuditEntries { get; }
}
