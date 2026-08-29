using CTMS.Application.Audit;
using CTMS.Application.Common;
using CTMS.Domain.Audit;
using CTMS.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace CTMS.Infrastructure.Persistence.Repositories;

public sealed class AuditRepository : IAuditRepository
{
    private readonly IMongoCollection<AuditEntry> _entries;

    public AuditRepository(IMongoContext context) => _entries = context.AuditEntries;

    public async Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        => await _entries.InsertOneAsync(entry, cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<AuditEntry>> ListByEntityAsync(
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken = default)
        => await _entries.Find(a => a.EntityType == entityType && a.EntityId == entityId)
            .SortByDescending(a => a.Timestamp)
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<AuditEntry>> ListByProjectAsync(
        Guid projectId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var total = (int)await _entries.CountDocumentsAsync(a => a.ProjectId == projectId, cancellationToken: cancellationToken);

        var items = await _entries.Find(a => a.ProjectId == projectId)
            .SortByDescending(a => a.Timestamp)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditEntry>(items, total);
    }
}
