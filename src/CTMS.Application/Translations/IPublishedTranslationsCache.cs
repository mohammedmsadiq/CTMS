namespace CTMS.Application.Translations;

/// <summary>The cached, assembled translation map for one <c>(application, language)</c> pair.</summary>
public sealed record CachedTranslations(IReadOnlyDictionary<string, string> Translations, string Hash);

/// <summary>
/// Read-through cache for assembled published translations. Backed by Redis in deployed
/// environments and by an in-process distributed-memory cache when no Redis connection string is
/// configured. The cache is an optimisation, never a source of truth: a miss — or an unreachable
/// backend — simply falls through to an on-demand assembly.
/// </summary>
/// <remarks>
/// Key format: <c>translations:{applicationCode}:{languageCode}</c> (both trimmed and
/// lower-cased). Publishing invalidates the affected keys; publishing a shared application fans
/// the invalidation out across every application (the service computes the pair list).
/// </remarks>
public interface IPublishedTranslationsCache
{
    Task<CachedTranslations?> GetAsync(
        string applicationCode,
        string languageCode,
        CancellationToken cancellationToken = default);

    Task SetAsync(
        string applicationCode,
        string languageCode,
        CachedTranslations value,
        CancellationToken cancellationToken = default);

    Task InvalidateAsync(
        string applicationCode,
        string languageCode,
        CancellationToken cancellationToken = default);
}
