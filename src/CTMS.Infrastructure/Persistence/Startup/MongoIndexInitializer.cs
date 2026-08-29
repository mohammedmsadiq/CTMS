using CTMS.Domain.Audit;
using CTMS.Domain.Locales;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;
using CTMS.Infrastructure.Persistence.Mongo;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;

namespace CTMS.Infrastructure.Persistence.Startup;

/// <summary>
/// Creates every collection index CTMS relies on. MongoDB's <c>createIndexes</c> is
/// idempotent, so this is safe to run on every startup.
/// </summary>
public sealed class MongoIndexInitializer : IHostedService
{
    private readonly IMongoContext _context;

    public MongoIndexInitializer(IMongoContext context) => _context = context;

    public Task StartAsync(CancellationToken cancellationToken) => EnsureIndexesAsync(_context, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static async Task EnsureIndexesAsync(IMongoContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var unique = new CreateIndexOptions { Unique = true };

        await context.Projects.Indexes.CreateOneAsync(
            new CreateIndexModel<Project>(
                Builders<Project>.IndexKeys.Ascending(p => p.Slug),
                unique),
            cancellationToken: cancellationToken);

        await context.Locales.Indexes.CreateOneAsync(
            new CreateIndexModel<Locale>(
                Builders<Locale>.IndexKeys.Ascending(l => l.ProjectId).Ascending(l => l.Code),
                unique),
            cancellationToken: cancellationToken);

        await context.TranslationKeys.Indexes.CreateOneAsync(
            new CreateIndexModel<TranslationKey>(
                Builders<TranslationKey>.IndexKeys.Ascending(k => k.ProjectId).Ascending(k => k.KeyName),
                unique),
            cancellationToken: cancellationToken);

        await context.TranslationStrings.Indexes.CreateManyAsync(
            new[]
            {
                new CreateIndexModel<TranslationString>(
                    Builders<TranslationString>.IndexKeys.Ascending(s => s.TranslationKeyId).Ascending(s => s.LocaleId),
                    unique),

                // Backs the project-wide review-state listing: filter by key set (+ optional
                // review state), sorted newest-updated first.
                new CreateIndexModel<TranslationString>(
                    Builders<TranslationString>.IndexKeys
                        .Ascending(s => s.TranslationKeyId)
                        .Ascending(s => s.ReviewState)
                        .Descending(s => s.UpdatedAt)),
            },
            cancellationToken: cancellationToken);

        await context.TranslationBundles.Indexes.CreateOneAsync(
            new CreateIndexModel<TranslationBundle>(
                Builders<TranslationBundle>.IndexKeys
                    .Ascending(b => b.ProjectId)
                    .Ascending(b => b.LocaleCode)
                    .Ascending(b => b.Version),
                unique),
            cancellationToken: cancellationToken);

        await context.AuditEntries.Indexes.CreateManyAsync(
            new[]
            {
                new CreateIndexModel<AuditEntry>(
                    Builders<AuditEntry>.IndexKeys.Ascending(a => a.ProjectId).Ascending(a => a.Timestamp)),
                new CreateIndexModel<AuditEntry>(
                    Builders<AuditEntry>.IndexKeys
                        .Ascending(a => a.EntityType)
                        .Ascending(a => a.EntityId)
                        .Ascending(a => a.Timestamp)),
            },
            cancellationToken: cancellationToken);
    }
}
