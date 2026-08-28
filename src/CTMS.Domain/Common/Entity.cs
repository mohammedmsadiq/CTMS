namespace CTMS.Domain.Common;

/// <summary>
/// Base type for persistent entities: a surrogate <see cref="Id"/> plus audit timestamps
/// that the persistence layer maintains on save.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    public DateTime CreatedAt { get; internal set; }

    public DateTime UpdatedAt { get; internal set; }
}
