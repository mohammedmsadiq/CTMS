namespace CTMS.Application.Audit;

/// <summary>Read model for a single audit-log entry.</summary>
public sealed record AuditEntryDto(
    Guid Id,
    Guid ProjectId,
    string EntityType,
    Guid EntityId,
    string Action,
    string Actor,
    DateTime Timestamp,
    string? FromState,
    string? ToState,
    string? Detail);
