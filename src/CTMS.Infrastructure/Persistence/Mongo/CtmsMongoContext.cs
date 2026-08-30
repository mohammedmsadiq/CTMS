using CTMS.Domain.Audit;
using CTMS.Domain.Languages;
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
    public const string LanguagesCollection = "languages";
    public const string TranslationKeysCollection = "translationKeys";
    public const string TranslationStringsCollection = "translationStrings";
    public const string AuditEntriesCollection = "auditEntries";

    public CtmsMongoContext(IMongoClient client, string databaseName)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        MongoMappingRegistration.Register();

        Database = client.GetDatabase(databaseName);
        Projects = Database.GetCollection<Project>(ProjectsCollection);
        Languages = Database.GetCollection<Language>(LanguagesCollection);
        TranslationKeys = Database.GetCollection<TranslationKey>(TranslationKeysCollection);
        TranslationStrings = Database.GetCollection<TranslationString>(TranslationStringsCollection);
        AuditEntries = Database.GetCollection<AuditEntry>(AuditEntriesCollection);
    }

    public IMongoDatabase Database { get; }

    public IMongoCollection<Project> Projects { get; }

    public IMongoCollection<Language> Languages { get; }

    public IMongoCollection<TranslationKey> TranslationKeys { get; }

    public IMongoCollection<TranslationString> TranslationStrings { get; }

    public IMongoCollection<AuditEntry> AuditEntries { get; }
}
