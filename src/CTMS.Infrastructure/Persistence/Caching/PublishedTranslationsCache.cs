using System.Text.Json;
using CTMS.Application.Translations;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CTMS.Infrastructure.Persistence.Caching;

/// <summary>
/// <see cref="IPublishedTranslationsCache"/> over <see cref="IDistributedCache"/> (Redis, or an
/// in-process distributed-memory cache locally). Stores the serialised map plus its content hash
/// so an <c>If-None-Match</c> / <c>304</c> check needs no assembly. Every backend call is
/// wrapped: a cache failure is logged and treated as a miss so delivery degrades to on-demand
/// assembly.
/// </summary>
public sealed class PublishedTranslationsCache : IPublishedTranslationsCache
{
    private const string KeyPrefix = "translations:";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;
    private readonly ILogger<PublishedTranslationsCache> _logger;
    private readonly TimeSpan _ttl;

    public PublishedTranslationsCache(
        IDistributedCache cache,
        IOptions<TranslationsCacheOptions> options,
        ILogger<PublishedTranslationsCache> logger)
    {
        _cache = cache;
        _logger = logger;

        var minutes = options.Value.TranslationsTtlMinutes;
        _ttl = TimeSpan.FromMinutes(minutes <= 0 ? TranslationsCacheOptions.DefaultTtlMinutes : minutes);
    }

    /// <summary>The cache key for a pair: <c>translations:{applicationCode}:{languageCode}</c>, both lower-cased.</summary>
    public static string KeyFor(string applicationCode, string languageCode)
        => $"{KeyPrefix}{Normalise(applicationCode)}:{Normalise(languageCode)}";

    public async Task<CachedTranslations?> GetAsync(
        string applicationCode,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(applicationCode) || string.IsNullOrWhiteSpace(languageCode))
        {
            return null;
        }

        byte[]? payload;
        try
        {
            payload = await _cache.GetAsync(KeyFor(applicationCode, languageCode), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Translations cache read failed for {Application}/{Language}; assembling on demand.",
                applicationCode,
                languageCode);
            return null;
        }

        if (payload is null || payload.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CachedTranslations>(payload, SerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Discarding an unreadable cached translation map for {Application}/{Language}.",
                applicationCode,
                languageCode);
            return null;
        }
    }

    public async Task SetAsync(
        string applicationCode,
        string languageCode,
        CachedTranslations value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (string.IsNullOrWhiteSpace(applicationCode) || string.IsNullOrWhiteSpace(languageCode))
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
            await _cache.SetAsync(
                KeyFor(applicationCode, languageCode),
                payload,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _ttl },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Translations cache write failed for {Application}/{Language}; continuing without caching.",
                applicationCode,
                languageCode);
        }
    }

    public async Task InvalidateAsync(
        string applicationCode,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(applicationCode) || string.IsNullOrWhiteSpace(languageCode))
        {
            return;
        }

        try
        {
            await _cache.RemoveAsync(KeyFor(applicationCode, languageCode), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Translations cache invalidation failed for {Application}/{Language}.",
                applicationCode,
                languageCode);
        }
    }

    private static string Normalise(string value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();
}
