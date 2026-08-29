using CTMS.Application.Common;

namespace CTMS.Infrastructure.Persistence.Mongo;

/// <summary>
/// MongoDB has no transaction to commit for the single-document writes CTMS performs — each
/// repository call is already durable when it returns. This implementation therefore does
/// nothing; the services keep calling it so their use cases still read as a unit of work.
/// </summary>
public sealed class NoOpUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0);
}
