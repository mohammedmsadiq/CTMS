using CTMS.Domain.Audit;
using CTMS.Domain.Languages;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;
using MongoDB.Driver;

namespace CTMS.Infrastructure.Persistence.Mongo;

/// <summary>Typed access to the CTMS MongoDB collections.</summary>
public interface IMongoContext
{
    IMongoDatabase Database { get; }

    IMongoCollection<Project> Projects { get; }

    IMongoCollection<Language> Languages { get; }

    IMongoCollection<TranslationKey> TranslationKeys { get; }

    IMongoCollection<TranslationString> TranslationStrings { get; }

    IMongoCollection<AuditEntry> AuditEntries { get; }
}
