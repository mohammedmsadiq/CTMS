namespace CTMS.Application.Common;

/// <summary>
/// Marks the end of a use-case's writes. The MongoDB persistence layer applies each
/// repository write immediately as an atomic single-document operation, so the concrete
/// implementation is a no-op kept only so the application services read as a unit of work.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
