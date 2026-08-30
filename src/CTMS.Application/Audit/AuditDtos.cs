namespace CTMS.Application.Audit;

/// <summary>Read model for a single audit-log entry.</summary>
/// <remarks>
/// <see cref="ApplicationId"/> is the owning application's id (internally the <c>Project.Id</c>).
/// </remarks>
public sealed record AuditEntryDto(
    Guid Id,
    Guid ApplicationId,
    string EntityType,
    Guid EntityId,
    string Action,
    string Actor,
    DateTime Timestamp,
    string? FromState,
    string? ToState,
    string? Detail,
    string? OldValue,
    string? NewValue);
