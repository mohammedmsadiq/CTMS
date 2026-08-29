using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace CTMS.Client.Caching;

/// <summary>Process-lifetime, thread-safe bundle cache. The default when no cache directory is set.</summary>
public sealed class InMemoryBundleStore : IBundleStore
{
    private readonly ConcurrentDictionary<string, StoredBundle> _entries = new(StringComparer.Ordinal);

    public Task<StoredBundle?> GetAsync(Guid projectId, string cacheKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_entries.TryGetValue(Key(projectId, cacheKey), out var stored) ? stored.Clone() : null);
    }

    public Task SetAsync(Guid projectId, string cacheKey, StoredBundle bundle, CancellationToken cancellationToken = default)
    {
        if (bundle is null)
        {
            throw new ArgumentNullException(nameof(bundle));
        }

        cancellationToken.ThrowIfCancellationRequested();
        _entries[Key(projectId, cacheKey)] = bundle.Clone();
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid projectId, string cacheKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entries.TryRemove(Key(projectId, cacheKey), out _);
        return Task.CompletedTask;
    }

    private static string Key(Guid projectId, string cacheKey) => string.Concat(projectId.ToString("D"), "/", cacheKey);
}
