using CTMS.Application.Common;
using CTMS.Domain.Audit;

namespace CTMS.Application.Audit;

/// <summary>Append-only persistence for the <see cref="AuditEntry"/> log.</summary>
public interface IAuditRepository
{
    /// <summary>Stores a new audit entry. Entries are never updated or removed.</summary>
    Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Every entry for one entity, newest first.</summary>
    Task<IReadOnlyList<AuditEntry>> ListByEntityAsync(
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken = default);

    /// <summary>One page of a project's entries, newest first, together with the total count.</summary>
    Task<PagedResult<AuditEntry>> ListByProjectAsync(
        Guid projectId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}
