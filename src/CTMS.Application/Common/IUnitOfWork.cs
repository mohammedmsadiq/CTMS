namespace CTMS.Application.Common;

/// <summary>Commits changes made through the repositories as a single transaction.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
