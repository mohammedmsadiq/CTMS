using CTMS.Domain.Common;
using CTMS.Domain.Translations;

namespace CTMS.Domain.Audit;

/// <summary>
/// An append-only record of a single state-changing operation on a domain entity. Audit
/// entries are never updated or deleted.
/// </summary>
public sealed class AuditEntry : Entity
{
    private AuditEntry()
    {
        // Materialization constructor for the persistence layer.
    }

    public AuditEntry(
        Guid projectId,
        string entityType,
        Guid entityId,
        AuditAction action,
        string actor,
        ReviewState? fromState = null,
        ReviewState? toState = null,
        string? detail = null)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("An audit entry must belong to a project.", nameof(projectId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);

        if (entityId == Guid.Empty)
        {
            throw new ArgumentException("An audit entry must reference an entity.", nameof(entityId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        ProjectId = projectId;
        EntityType = entityType.Trim();
        EntityId = entityId;
        Action = action;
        Actor = actor.Trim();
        FromState = fromState;
        ToState = toState;
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
        Timestamp = DateTime.UtcNow;
    }

    public Guid ProjectId { get; private set; }

    /// <summary>The audited entity's type name, e.g. <c>"TranslationString"</c>.</summary>
    public string EntityType { get; private set; } = string.Empty;

    public Guid EntityId { get; private set; }

    public AuditAction Action { get; private set; }

    public string Actor { get; private set; } = string.Empty;

    /// <summary>When the operation happened (UTC).</summary>
    public DateTime Timestamp { get; private set; }

    /// <summary>Review state before the operation, when the operation changed review state.</summary>
    public ReviewState? FromState { get; private set; }

    /// <summary>Review state after the operation, when the operation changed review state.</summary>
    public ReviewState? ToState { get; private set; }

    /// <summary>Optional free-form context (e.g. the new value, or a note).</summary>
    public string? Detail { get; private set; }
}
