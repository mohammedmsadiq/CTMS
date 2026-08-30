using CTMS.Domain.Translations;

namespace CTMS.Domain.Audit;

/// <summary>
/// An append-only record of a single state-changing operation on a domain entity. Audit
/// entries are never updated or deleted, so — unlike the mutable aggregates — this type does
/// not derive from <see cref="CTMS.Domain.Common.Entity"/>: it carries only an <see cref="Id"/>
/// and a <see cref="Timestamp"/>, with no <c>CreatedAt</c>/<c>UpdatedAt</c> bookkeeping.
/// </summary>
public sealed class AuditEntry
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
        string? detail = null,
        string? oldValue = null,
        string? newValue = null)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("An audit entry must belong to an application.", nameof(projectId));
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
        OldValue = oldValue;
        NewValue = newValue;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>Surrogate identity, assigned on construction and mapped to Mongo's <c>_id</c>.</summary>
    public Guid Id { get; private set; } = Guid.NewGuid();

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

    /// <summary>Optional free-form context (e.g. a note or bundle detail).</summary>
    public string? Detail { get; private set; }

    /// <summary>The value before an <see cref="AuditAction.Edited"/> operation, when one changed.</summary>
    public string? OldValue { get; private set; }

    /// <summary>The value after a <see cref="AuditAction.Created"/> or <see cref="AuditAction.Edited"/> operation.</summary>
    public string? NewValue { get; private set; }
}
