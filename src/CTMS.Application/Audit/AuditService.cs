using CTMS.Application.Common;
using CTMS.Application.Projects;
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
    private readonly IProjectRepository _projects;

    public AuditService(IAuditRepository audit, IProjectRepository projects)
    {
        _audit = audit;
        _projects = projects;
    }

    public async Task<IReadOnlyList<AuditEntryDto>> ListByEntityAsync(
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken = default)
    {
        var entries = await _audit.ListByEntityAsync(entityType, entityId, cancellationToken);
        return entries.Select(ToDto).ToList();
    }

    /// <summary>One page of an application's audit feed, newest first. <c>null</c> if the application is unknown.</summary>
    public async Task<PagedResult<AuditEntryDto>?> ListByApplicationAsync(
        string applicationCode,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetBySlugAsync(Slug.From(applicationCode ?? string.Empty), cancellationToken);
        if (project is null)
        {
            return null;
        }

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

        var page = await _audit.ListByProjectAsync(project.Id, skip, take, cancellationToken);
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
        entry.Detail,
        entry.OldValue,
        entry.NewValue);
}
