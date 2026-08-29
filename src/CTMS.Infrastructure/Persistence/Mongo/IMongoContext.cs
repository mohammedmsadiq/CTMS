using CTMS.Domain.Audit;
using CTMS.Domain.Locales;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;
using MongoDB.Driver;

namespace CTMS.Infrastructure.Persistence.Mongo;

/// <summary>Typed access to the CTMS MongoDB collections.</summary>
public interface IMongoContext
{
    IMongoDatabase Database { get; }

    IMongoCollection<Project> Projects { get; }

    IMongoCollection<Locale> Locales { get; }

    IMongoCollection<TranslationKey> TranslationKeys { get; }

    IMongoCollection<TranslationString> TranslationStrings { get; }

    IMongoCollection<TranslationBundle> TranslationBundles { get; }

    IMongoCollection<AuditEntry> AuditEntries { get; }
}
