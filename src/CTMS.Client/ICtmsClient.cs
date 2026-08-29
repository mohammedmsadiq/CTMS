using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CTMS.Client;

/// <summary>
/// Fetches published translation bundles from the CTMS API, caches them locally, and resolves keys
/// through a locale fallback chain. Fetches never block on the network when a cached copy exists.
/// </summary>
public interface ICtmsClient
{
    /// <summary>
    /// Returns the latest bundle for <paramref name="locale"/>, revalidating against the API:
    /// a cached copy inside the staleness window is returned directly; otherwise the call sends
    /// <c>If-None-Match</c> and a <c>304</c> refreshes the cached copy while a <c>200</c> replaces
    /// it. If the API is unreachable a cached copy is returned with <see cref="TranslationBundle.IsStale"/>
    /// set; with no cache a <see cref="CtmsOfflineException"/> is thrown.
    /// </summary>
    Task<TranslationBundle> GetBundleAsync(string locale, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a specific immutable bundle version. Cached forever once fetched and never
    /// revalidated (no <c>If-None-Match</c>).
    /// </summary>
    Task<TranslationBundle> GetBundleAsync(string locale, int version, CancellationToken cancellationToken = default);

    /// <summary>Lists the published version history for <paramref name="locale"/>, oldest first.</summary>
    Task<IReadOnlyList<BundleVersion>> GetVersionsAsync(string locale, CancellationToken cancellationToken = default);

    /// <summary>
    /// Warms the cache and the in-memory resolver for each locale. Per-locale failures are logged
    /// and swallowed so a warm-up never throws.
    /// </summary>
    Task PrefetchAsync(IEnumerable<string> locales, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves <paramref name="key"/> against bundles already loaded by
    /// <see cref="GetBundleAsync(string, CancellationToken)"/> / <see cref="PrefetchAsync"/>, walking
    /// exact locale → parent locales → configured default locale. Returns <c>null</c> if unresolved.
    /// </summary>
    string? Get(string key, string locale);

    /// <summary>
    /// Like <see cref="Get(string, string)"/> but with extra fallback locales inserted before the
    /// default, and a guaranteed non-null result: the configured missing-key fallback, or the key
    /// itself, when nothing resolves.
    /// </summary>
    string Get(string key, string locale, params string[] fallbackLocales);
}
