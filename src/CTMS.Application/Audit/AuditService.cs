using CTMS.Application.Common;
using CTMS.Domain.Audit;

namespace CTMS.Application.Audit;

/// <summary>
/// Read access to the audit log. Writes are performed inline by the services that own the
/// audited operations (see <c>TranslationStringService</c>); this service only projects.
/// </summary>
public sealed class AuditService
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly IAuditRepository _audit;

    public AuditService(IAuditRepository audit) => _audit = audit;

    public async Task<IReadOnlyList<AuditEntryDto>> ListByEntityAsync(
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken = default)
    {
        var entries = await _audit.ListByEntityAsync(entityType, entityId, cancellationToken);
        return entries.Select(ToDto).ToList();
    }

    public async Task<PagedResult<AuditEntryDto>> ListByProjectAsync(
        Guid projectId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0)
        {
            skip = 0;
        }

        take = take switch
        {
            <= 0 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => take,
        };

        var page = await _audit.ListByProjectAsync(projectId, skip, take, cancellationToken);
        return new PagedResult<AuditEntryDto>(page.Items.Select(ToDto).ToList(), page.Total);
    }

    private static AuditEntryDto ToDto(AuditEntry entry) => new(
        entry.Id,
        entry.ProjectId,
        entry.EntityType,
        entry.EntityId,
        entry.Action.ToString(),
        entry.Actor,
        entry.Timestamp,
        entry.FromState?.ToString(),
        entry.ToState?.ToString(),
        entry.Detail);
}
