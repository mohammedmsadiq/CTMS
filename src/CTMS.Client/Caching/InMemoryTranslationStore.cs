using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace CTMS.Client.Caching;

/// <summary>Process-lifetime, thread-safe translation cache. The default when no cache directory is set.</summary>
public sealed class InMemoryTranslationStore : ITranslationStore
{
    private readonly ConcurrentDictionary<string, StoredTranslations> _entries = new(StringComparer.Ordinal);

    public Task<StoredTranslations?> GetAsync(string application, string language, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_entries.TryGetValue(Key(application, language), out var stored) ? stored.Clone() : null);
    }

    public Task SetAsync(string application, string language, StoredTranslations value, CancellationToken cancellationToken = default)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        cancellationToken.ThrowIfCancellationRequested();
        _entries[Key(application, language)] = value.Clone();
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string application, string language, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entries.TryRemove(Key(application, language), out _);
        return Task.CompletedTask;
    }

    private static string Key(string application, string language) =>
        string.Concat(application.Trim().ToLowerInvariant(), "/", language.Trim().ToLowerInvariant());
}
