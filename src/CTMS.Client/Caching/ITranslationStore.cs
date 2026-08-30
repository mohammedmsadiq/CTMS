using System.Threading;
using System.Threading.Tasks;

namespace CTMS.Client.Caching;

/// <summary>
/// Persistence for downloaded translation sets. Implementations must be safe for concurrent use and
/// must treat any read failure (missing, corrupt, unreadable) as a cache miss rather than throwing;
/// a write failure must never break the caller.
/// </summary>
/// <remarks>
/// <paramref name="application"/> and <paramref name="language"/> are passed as the SDK received
/// them; implementations normalise (lower-case) internally.
/// </remarks>
public interface ITranslationStore
{
    Task<StoredTranslations?> GetAsync(string application, string language, CancellationToken cancellationToken = default);

    Task SetAsync(string application, string language, StoredTranslations value, CancellationToken cancellationToken = default);

    Task RemoveAsync(string application, string language, CancellationToken cancellationToken = default);
}
