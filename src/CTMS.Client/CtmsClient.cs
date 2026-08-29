using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CTMS.Client.Caching;
using CTMS.Client.Internal;

namespace CTMS.Client;

/// <summary>
/// Default <see cref="ICtmsClient"/>. Thread-safe. Construct one per project and reuse it.
/// </summary>
public sealed class CtmsClient : ICtmsClient, IDisposable
{
    private readonly CtmsClientOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly IBundleStore _store;
    private readonly Func<DateTimeOffset> _clock;

    // Latest successfully materialised bundle per locale, for the synchronous Get(...) resolver.
    private readonly ConcurrentDictionary<string, TranslationBundle> _resolved =
        new(StringComparer.OrdinalIgnoreCase);

    public CtmsClient(CtmsClientOptions options)
        : this(options, null, null, null)
    {
    }

    public CtmsClient(CtmsClientOptions options, HttpClient httpClient)
        : this(options, httpClient, null, null)
    {
    }

    public CtmsClient(CtmsClientOptions options, HttpClient? httpClient, IBundleStore? store)
        : this(options, httpClient, store, null)
    {
    }

    internal CtmsClient(CtmsClientOptions options, HttpClient? httpClient, IBundleStore? store, Func<DateTimeOffset>? clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (_options.ProjectId == Guid.Empty)
        {
            throw new ArgumentException("CtmsClientOptions.ProjectId is required.", nameof(options));
        }

        var resolvedHttp = httpClient ?? _options.HttpClient;
        if (resolvedHttp is null)
        {
            if (_options.BaseAddress is null)
            {
                throw new ArgumentException(
                    "CtmsClientOptions.BaseAddress is required when no HttpClient is supplied.",
                    nameof(options));
            }

            resolvedHttp = new HttpClient { BaseAddress = EnsureTrailingSlash(_options.BaseAddress) };
            if (_options.RequestTimeout > TimeSpan.Zero)
            {
                // A generous ceiling; per-request timeouts are enforced with a linked token below.
                resolvedHttp.Timeout = Timeout.InfiniteTimeSpan;
            }

            _ownsHttp = true;
        }
        else if (resolvedHttp.BaseAddress is null && _options.BaseAddress is not null)
        {
            resolvedHttp.BaseAddress = EnsureTrailingSlash(_options.BaseAddress);
        }

        _http = resolvedHttp;
        _store = store
            ?? _options.BundleStore
            ?? (string.IsNullOrWhiteSpace(_options.CacheDirectory)
                ? new InMemoryBundleStore()
                : new FileBundleStore(_options.CacheDirectory!));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public async Task<TranslationBundle> GetBundleAsync(string locale, CancellationToken cancellationToken = default)
    {
        RequireLocale(locale);
        var cacheKey = LatestKey(locale);
        var cached = await _store.GetAsync(_options.ProjectId, cacheKey, cancellationToken).ConfigureAwait(false);
        var now = _clock();

        if (cached is not null && now - cached.LastValidatedAt < _options.StalenessTtl)
        {
            return Remember(locale, TranslationBundle.FromStored(cached, isStale: false));
        }

        HttpResponseMessage response;
        try
        {
            response = await SendAsync(BundlePath(locale), cached?.Etag, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            if (cached is not null)
            {
                Log($"offline: serving cached bundle '{locale}' v{cached.Version} as stale ({ex.GetType().Name}).");
                return Remember(locale, TranslationBundle.FromStored(cached, isStale: true));
            }

            throw new CtmsOfflineException(
                $"No cached bundle for locale '{locale}' and the CTMS API could not be reached.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                if (cached is null)
                {
                    throw new CtmsApiException(
                        304, "Not Modified", "The API returned 304 but no cached bundle is available.");
                }

                cached.LastValidatedAt = now;
                await _store.SetAsync(_options.ProjectId, cacheKey, cached, cancellationToken).ConfigureAwait(false);
                Log($"revalidated bundle '{locale}' v{cached.Version} (304).");
                return Remember(locale, TranslationBundle.FromStored(cached, isStale: false));
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await ToApiExceptionAsync(response, cancellationToken).ConfigureAwait(false);
            }

            var wire = await ReadJsonAsync<BundleWire>(response, cancellationToken).ConfigureAwait(false);
            var stored = ToStored(wire, retrievedAt: now, lastValidatedAt: now);
            await _store.SetAsync(_options.ProjectId, cacheKey, stored, cancellationToken).ConfigureAwait(false);
            Log($"fetched bundle '{locale}' v{stored.Version} (200).");
            return Remember(locale, TranslationBundle.FromStored(stored, isStale: false));
        }
    }

    /// <inheritdoc />
    public async Task<TranslationBundle> GetBundleAsync(string locale, int version, CancellationToken cancellationToken = default)
    {
        RequireLocale(locale);
        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Bundle versions start at 1.");
        }

        var cacheKey = PinnedKey(locale, version);
        var cached = await _store.GetAsync(_options.ProjectId, cacheKey, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return Remember(locale, TranslationBundle.FromStored(cached, isStale: false));
        }

        HttpResponseMessage response;
        try
        {
            response = await SendAsync(VersionPath(locale, version), ifNoneMatch: null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw new CtmsOfflineException(
                $"Pinned bundle '{locale}' v{version} is not cached and the CTMS API could not be reached.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await ToApiExceptionAsync(response, cancellationToken).ConfigureAwait(false);
            }

            var now = _clock();
            var wire = await ReadJsonAsync<BundleWire>(response, cancellationToken).ConfigureAwait(false);
            var stored = ToStored(wire, retrievedAt: now, lastValidatedAt: now);
            await _store.SetAsync(_options.ProjectId, cacheKey, stored, cancellationToken).ConfigureAwait(false);
            return Remember(locale, TranslationBundle.FromStored(stored, isStale: false));
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BundleVersion>> GetVersionsAsync(string locale, CancellationToken cancellationToken = default)
    {
        RequireLocale(locale);

        HttpResponseMessage response;
        try
        {
            response = await SendAsync(VersionsPath(locale), ifNoneMatch: null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw new CtmsOfflineException(
                $"Version history for locale '{locale}' is unavailable: the CTMS API could not be reached.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await ToApiExceptionAsync(response, cancellationToken).ConfigureAwait(false);
            }

            var wire = await ReadJsonAsync<List<BundleVersionWire>>(response, cancellationToken).ConfigureAwait(false)
                       ?? new List<BundleVersionWire>();

            var result = new List<BundleVersion>(wire.Count);
            foreach (var v in wire)
            {
                result.Add(new BundleVersion(v.Version, v.ETag, v.CreatedAt, v.CreatedBy, v.EntryCount));
            }

            return result;
        }
    }

    /// <inheritdoc />
    public async Task PrefetchAsync(IEnumerable<string> locales, CancellationToken cancellationToken = default)
    {
        if (locales is null)
        {
            throw new ArgumentNullException(nameof(locales));
        }

        foreach (var locale in locales)
        {
            if (string.IsNullOrWhiteSpace(locale))
            {
                continue;
            }

            try
            {
                await GetBundleAsync(locale, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (CtmsException ex)
            {
                Log($"prefetch of '{locale}' failed: {ex.Message}");
            }
        }
    }

    /// <inheritdoc />
    public string? Get(string key, string locale)
    {
        RequireKey(key);
        RequireLocale(locale);
        return Resolve(key, locale, Array.Empty<string>());
    }

    /// <inheritdoc />
    public string Get(string key, string locale, params string[] fallbackLocales)
    {
        RequireKey(key);
        RequireLocale(locale);
        return Resolve(key, locale, fallbackLocales ?? Array.Empty<string>())
               ?? _options.MissingKeyFallback?.Invoke(key)
               ?? key;
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    private string? Resolve(string key, string locale, IReadOnlyList<string> fallbackLocales)
    {
        foreach (var candidate in LocaleChain.Build(locale, fallbackLocales, _options.DefaultLocale))
        {
            if (_resolved.TryGetValue(candidate, out var bundle) && bundle.Entries.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private TranslationBundle Remember(string requestLocale, TranslationBundle bundle)
    {
        _resolved[requestLocale.Trim()] = bundle;
        if (!string.IsNullOrEmpty(bundle.LocaleCode))
        {
            _resolved[bundle.LocaleCode.Trim()] = bundle;
        }

        return bundle;
    }

    private async Task<HttpResponseMessage> SendAsync(string relativeUrl, string? ifNoneMatch, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrEmpty(ifNoneMatch))
        {
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue($"\"{ifNoneMatch}\""));
        }

        var token = await ResolveTokenAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        CancellationTokenSource? timeoutCts = null;
        if (_options.RequestTimeout > TimeSpan.Zero)
        {
            timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.RequestTimeout);
        }

        try
        {
            return await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts?.Token ?? cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            timeoutCts?.Dispose();
        }
    }

    private async Task<string?> ResolveTokenAsync(CancellationToken cancellationToken)
    {
        if (_options.AuthTokenProvider is not null)
        {
            return await _options.AuthTokenProvider(cancellationToken).ConfigureAwait(false);
        }

        return _options.AuthToken;
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
#if NET5_0_OR_GREATER
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
        return await JsonSerializer.DeserializeAsync<T>(stream, CtmsJson.Options, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CtmsApiException> ToApiExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        string? title = response.ReasonPhrase;
        string? detail = null;

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (string.Equals(mediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var problem = await ReadJsonAsync<ProblemWire>(response, cancellationToken).ConfigureAwait(false);
                if (problem is not null)
                {
                    if (!string.IsNullOrWhiteSpace(problem.Title))
                    {
                        title = problem.Title;
                    }

                    detail = problem.Detail;
                }
            }
            catch (JsonException)
            {
                // Fall back to the status line.
            }
        }

        return new CtmsApiException(status, title, detail);
    }

    private StoredBundle ToStored(BundleWire? wire, DateTimeOffset retrievedAt, DateTimeOffset lastValidatedAt)
    {
        if (wire is null)
        {
            throw new CtmsApiException(200, "Malformed response", "The CTMS API returned an empty bundle body.");
        }

        return new StoredBundle
        {
            ProjectId = wire.ProjectId == Guid.Empty ? _options.ProjectId : wire.ProjectId,
            LocaleCode = wire.LocaleCode,
            Version = wire.Version,
            Entries = new Dictionary<string, string>(wire.Entries, StringComparer.Ordinal),
            Etag = wire.ETag,
            CreatedBy = string.IsNullOrEmpty(wire.CreatedBy) ? null : wire.CreatedBy,
            CreatedAt = wire.CreatedAt == default ? null : wire.CreatedAt,
            RetrievedAt = retrievedAt,
            LastValidatedAt = lastValidatedAt,
        };
    }

    private void Log(string message) => _options.DiagnosticsLogger?.Invoke("[CTMS.Client] " + message);

    private static bool IsTransportFailure(Exception ex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            // A genuine caller cancellation must propagate untouched.
            return false;
        }

        return ex is HttpRequestException
               or SocketException
               or IOException
               or TimeoutException
               or OperationCanceledException; // request timeout via the linked token
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var text = uri.AbsoluteUri;
        return text.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(text + "/");
    }

    private string BundlePath(string locale) =>
        $"api/projects/{_options.ProjectId:D}/bundles/{Uri.EscapeDataString(locale.Trim())}";

    private string VersionsPath(string locale) => BundlePath(locale) + "/versions";

    private string VersionPath(string locale, int version) => VersionsPath(locale) + "/" + version.ToString();

    private static string LatestKey(string locale) => locale.Trim().ToLowerInvariant();

    private static string PinnedKey(string locale, int version) => LatestKey(locale) + ".v" + version.ToString();

    private static void RequireLocale(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            throw new ArgumentException("A locale code is required.", nameof(locale));
        }
    }

    private static void RequireKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("A translation key is required.", nameof(key));
        }
    }
}
