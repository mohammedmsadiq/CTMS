using System.Text.Json;
using CTMS.Application.Translations;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CTMS.Infrastructure.Persistence.Caching;

/// <summary>
/// <see cref="IBundleCache"/> over <see cref="IDistributedCache"/> (Redis, or an in-process
/// distributed-memory cache locally). Stores the serialised <see cref="TranslationBundleDto"/>
/// - whose <see cref="TranslationBundleDto.ETag"/> member carries the content hash, so an
/// <c>If-None-Match</c> / <c>304</c> check needs no database round-trip. Every backend call is
/// wrapped: a cache failure is logged and treated as a miss so the service degrades to
/// MongoDB-only.
/// </summary>
public sealed class BundleCache : IBundleCache
{
    private const string KeyPrefix = "ctms:bundle:";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;
    private readonly ILogger<BundleCache> _logger;
    private readonly TimeSpan _ttl;

    public BundleCache(
        IDistributedCache cache,
        IOptions<BundleCacheOptions> options,
        ILogger<BundleCache> logger)
    {
        _cache = cache;
        _logger = logger;

        var minutes = options.Value.BundleTtlMinutes;
        _ttl = TimeSpan.FromMinutes(minutes <= 0 ? BundleCacheOptions.DefaultTtlMinutes : minutes);
    }

    /// <summary>
    /// The cache key for a pair: <c>ctms:bundle:{projectId}:{localeCode}:latest</c> with the
    /// locale code trimmed and lower-cased.
    /// </summary>
    public static string KeyFor(Guid projectId, string localeCode)
        => $"{KeyPrefix}{projectId}:{Normalise(localeCode)}:latest";

    public async Task<TranslationBundleDto?> GetLatestAsync(
        Guid projectId,
        string localeCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localeCode))
        {
            return null;
        }

        byte[]? payload;
        try
        {
            payload = await _cache.GetAsync(KeyFor(projectId, localeCode), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Bundle cache read failed for {ProjectId}/{LocaleCode}; falling through to MongoDB.",
                projectId,
                localeCode);
            return null;
        }

        if (payload is null || payload.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TranslationBundleDto>(payload, SerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Discarding an unreadable cached bundle for {ProjectId}/{LocaleCode}.",
                projectId,
                localeCode);
            return null;
        }
    }

    public async Task SetLatestAsync(
        Guid projectId,
        string localeCode,
        TranslationBundleDto bundle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        if (string.IsNullOrWhiteSpace(localeCode))
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(bundle, SerializerOptions);
            await _cache.SetAsync(
                KeyFor(projectId, localeCode),
                payload,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _ttl },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Bundle cache write failed for {ProjectId}/{LocaleCode}; continuing without caching.",
                projectId,
                localeCode);
        }
    }

    public async Task InvalidateAsync(
        Guid projectId,
        string localeCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localeCode))
        {
            return;
        }

        try
        {
            await _cache.RemoveAsync(KeyFor(projectId, localeCode), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Bundle cache invalidation failed for {ProjectId}/{LocaleCode}.",
                projectId,
                localeCode);
        }
    }

    private static string Normalise(string localeCode)
        => (localeCode ?? string.Empty).Trim().ToLowerInvariant();
}
