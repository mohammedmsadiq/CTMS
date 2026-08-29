namespace CTMS.Application.Translations;

/// <summary>
/// Read-through cache for the <em>latest</em> published bundle of a <c>(project, locale)</c> pair.
/// Backed by Redis in deployed environments and by an in-process distributed-memory cache when no
/// Redis connection string is configured. The cache is an optimisation, never a source of truth:
/// a miss - or an unreachable backend - simply falls through to MongoDB, and only present bundles
/// are ever stored (negative lookups are not cached).
/// </summary>
/// <remarks>
/// Implementations normalise <c>localeCode</c> (trim + lower-case) before keying, so callers may
/// pass either the request value or the canonical <see cref="Domain.Locales.Locale.Code"/>.
/// </remarks>
public interface IBundleCache
{
    /// <summary>The cached latest bundle for the pair, or <c>null</c> on a miss.</summary>
    Task<TranslationBundleDto?> GetLatestAsync(
        Guid projectId,
        string localeCode,
        CancellationToken cancellationToken = default);

    /// <summary>Stores <paramref name="bundle"/> as the latest bundle for the pair.</summary>
    Task SetLatestAsync(
        Guid projectId,
        string localeCode,
        TranslationBundleDto bundle,
        CancellationToken cancellationToken = default);

    /// <summary>Drops any cached latest bundle for the pair.</summary>
    Task InvalidateAsync(
        Guid projectId,
        string localeCode,
        CancellationToken cancellationToken = default);
}
