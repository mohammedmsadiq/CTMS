using System;
using System.Threading;
using System.Threading.Tasks;

namespace CTMS.Client.Caching;

/// <summary>
/// Persistence for downloaded bundles. Implementations must be safe for concurrent use and must
/// treat any read failure (missing, corrupt, unreadable) as a cache miss rather than throwing.
/// </summary>
/// <remarks>
/// <paramref name="cacheKey"/> is opaque and already normalised by the SDK: the lower-cased locale
/// for a "latest" bundle (e.g. <c>fr-ca</c>), or <c>{locale}.v{n}</c> for a pinned version.
/// </remarks>
public interface IBundleStore
{
    Task<StoredBundle?> GetAsync(Guid projectId, string cacheKey, CancellationToken cancellationToken = default);

    Task SetAsync(Guid projectId, string cacheKey, StoredBundle bundle, CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid projectId, string cacheKey, CancellationToken cancellationToken = default);
}
