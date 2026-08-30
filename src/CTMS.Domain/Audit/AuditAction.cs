namespace CTMS.Domain.Audit;

/// <summary>The kind of state-changing operation an <see cref="AuditEntry"/> records.</summary>
public enum AuditAction
{
    Created = 0,
    Edited = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
    Reopened = 5,
    Published = 6,
    Archived = 7,
    Unarchived = 8,
}
