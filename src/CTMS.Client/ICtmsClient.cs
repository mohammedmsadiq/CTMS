using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CTMS.Client;

/// <summary>
/// Fetches assembled-on-demand published translations from the CTMS API, caches them locally, and
/// resolves keys through a language fallback chain. Fetches never block on the network when a cached
/// copy exists.
/// </summary>
public interface ICtmsClient
{
    /// <summary>
    /// Returns the translation set for <paramref name="language"/>, revalidating against the API:
    /// a cached copy inside the staleness window is returned directly; otherwise the call sends
    /// <c>If-None-Match</c> with the cached <c>ETag</c> and a <c>304</c> refreshes the cached copy
    /// while a <c>200</c> replaces it. If the API is unreachable a cached copy is returned with
    /// <see cref="TranslationSet.IsStale"/> set; with no cache a <see cref="CtmsOfflineException"/>
    /// is thrown. An <c>application/problem+json</c> error becomes a <see cref="CtmsApiException"/>.
    /// The server fills missing keys from the language's server-side <c>FallbackCode</c> chain
    /// before responding.
    /// </summary>
    Task<TranslationSet> GetTranslationsAsync(string language, CancellationToken cancellationToken = default);

    /// <summary>
    /// Warms the cache and the in-memory resolver for each language. Per-language failures are
    /// logged and swallowed so a warm-up never throws (caller cancellation still propagates).
    /// </summary>
    Task PrefetchAsync(IEnumerable<string> languages, CancellationToken cancellationToken = default);

    /// <summary>Thin passthrough over <c>GET /api/languages</c> — the active language catalogue.</summary>
    Task<IReadOnlyList<LanguageInfo>> GetLanguagesAsync(CancellationToken cancellationToken = default);

    /// <summary>Thin passthrough over <c>GET /api/applications</c> — the active application catalogue.</summary>
    Task<IReadOnlyList<ApplicationInfo>> GetApplicationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves <paramref name="key"/> against sets already loaded by
    /// <see cref="GetTranslationsAsync"/> / <see cref="PrefetchAsync"/>, walking
    /// exact language → configured default language (case-insensitive). Returns <c>null</c> if
    /// unresolved. Never triggers a fetch.
    /// </summary>
    string? Get(string key, string language);

    /// <summary>
    /// Like <see cref="Get(string, string)"/> but with extra fallback languages inserted before the
    /// default, and a guaranteed non-null result: the configured missing-key fallback, or the key
    /// itself, when nothing resolves.
    /// </summary>
    string Get(string key, string language, params string[] extraFallbackLanguages);
}
