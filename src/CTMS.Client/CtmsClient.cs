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
/// Default <see cref="ICtmsClient"/>. Thread-safe. Construct one per application and reuse it.
/// </summary>
public sealed class CtmsClient : ICtmsClient, IDisposable
{
    private readonly CtmsClientOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly ITranslationStore _store;
    private readonly Func<DateTimeOffset> _clock;

    // Latest successfully materialised set per language, for the synchronous Get(...) resolver.
    private readonly ConcurrentDictionary<string, TranslationSet> _resolved =
        new(StringComparer.OrdinalIgnoreCase);

    public CtmsClient(CtmsClientOptions options)
        : this(options, null, null, null)
    {
    }

    public CtmsClient(CtmsClientOptions options, HttpClient httpClient)
        : this(options, httpClient, null, null)
    {
    }

    public CtmsClient(CtmsClientOptions options, HttpClient? httpClient, ITranslationStore? store)
        : this(options, httpClient, store, null)
    {
    }

    internal CtmsClient(CtmsClientOptions options, HttpClient? httpClient, ITranslationStore? store, Func<DateTimeOffset>? clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(_options.Application))
        {
            throw new ArgumentException("CtmsClientOptions.Application is required.", nameof(options));
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
            ?? _options.TranslationStore
            ?? (string.IsNullOrWhiteSpace(_options.CacheDirectory)
                ? new InMemoryTranslationStore()
                : new FileTranslationStore(_options.CacheDirectory!));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public async Task<TranslationSet> GetTranslationsAsync(string language, CancellationToken cancellationToken = default)
    {
        RequireLanguage(language);

        var app = _options.Application;
        var cached = await _store.GetAsync(app, language, cancellationToken).ConfigureAwait(false);
        var now = _clock();

        if (cached is not null && _options.StalenessTtl > TimeSpan.Zero && now - cached.LastValidatedAt < _options.StalenessTtl)
        {
            return Remember(language, TranslationSet.FromStored(cached, isStale: false));
        }

        HttpResponseMessage response;
        try
        {
            response = await SendAsync(HttpMethod.Get, TranslationsPath(language), cached?.Etag, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            if (cached is not null)
            {
                Log($"offline: serving cached translations '{app}/{language}' as stale ({ex.GetType().Name}).");
                return Remember(language, TranslationSet.FromStored(cached, isStale: true));
            }

            throw new CtmsOfflineException(
                $"No cached translations for application '{app}' language '{language}' and the CTMS API could not be reached.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                if (cached is null)
                {
                    throw new CtmsApiException(
                        304, "Not Modified", "The API returned 304 but no cached translations are available.");
                }

                cached.LastValidatedAt = now;
                await _store.SetAsync(app, language, cached, cancellationToken).ConfigureAwait(false);
                Log($"revalidated translations '{app}/{language}' (304).");
                return Remember(language, TranslationSet.FromStored(cached, isStale: false));
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await ToApiExceptionAsync(response, cancellationToken).ConfigureAwait(false);
            }

            var wire = await ReadJsonAsync<TranslationsWire>(response, cancellationToken).ConfigureAwait(false);
            var stored = ToStored(wire, response, language, retrievedAt: now, lastValidatedAt: now);
            await _store.SetAsync(app, language, stored, cancellationToken).ConfigureAwait(false);
            Log($"fetched translations '{app}/{language}' ({stored.Entries.Count} keys, 200).");
            return Remember(language, TranslationSet.FromStored(stored, isStale: false));
        }
    }

    /// <inheritdoc />
    public async Task PrefetchAsync(IEnumerable<string> languages, CancellationToken cancellationToken = default)
    {
        if (languages is null)
        {
            throw new ArgumentNullException(nameof(languages));
        }

        foreach (var language in languages)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                continue;
            }

            try
            {
                await GetTranslationsAsync(language, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (CtmsException ex)
            {
                Log($"prefetch of '{language}' failed: {ex.Message}");
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LanguageInfo>> GetLanguagesAsync(CancellationToken cancellationToken = default)
    {
        var wire = await GetCatalogueAsync<LanguageWire>("api/languages", cancellationToken).ConfigureAwait(false);
        var result = new List<LanguageInfo>(wire.Count);
        foreach (var l in wire)
        {
            result.Add(new LanguageInfo(l.Code, l.Name, string.IsNullOrEmpty(l.FallbackCode) ? null : l.FallbackCode, l.IsRtl, l.Active, l.CreatedAt, l.UpdatedAt));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApplicationInfo>> GetApplicationsAsync(CancellationToken cancellationToken = default)
    {
        var wire = await GetCatalogueAsync<ApplicationWire>("api/projects", cancellationToken).ConfigureAwait(false);
        var result = new List<ApplicationInfo>(wire.Count);
        foreach (var a in wire)
        {
            result.Add(new ApplicationInfo(
                a.Code,
                a.Name,
                string.IsNullOrEmpty(a.Description) ? null : a.Description,
                a.IsCommon,
                a.Active,
                a.BaseLanguageCode,
                a.EnabledLanguageCodes.AsReadOnly(),
                a.CreatedAt,
                a.UpdatedAt));
        }

        return result;
    }

    /// <inheritdoc />
    public string? Get(string key, string language)
    {
        RequireKey(key);
        RequireLanguage(language);
        return Resolve(key, language, Array.Empty<string>());
    }

    /// <inheritdoc />
    public string Get(string key, string language, params string[] extraFallbackLanguages)
    {
        RequireKey(key);
        RequireLanguage(language);
        return Resolve(key, language, extraFallbackLanguages ?? Array.Empty<string>())
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

    private string? Resolve(string key, string language, IReadOnlyList<string> extraFallbackLanguages)
    {
        foreach (var candidate in LanguageChain.Build(language, extraFallbackLanguages, _options.DefaultLanguage))
        {
            if (_resolved.TryGetValue(candidate, out var set) && set.Entries.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private TranslationSet Remember(string requestLanguage, TranslationSet set)
    {
        _resolved[requestLanguage.Trim()] = set;
        if (!string.IsNullOrEmpty(set.Language))
        {
            _resolved[set.Language.Trim()] = set;
        }

        return set;
    }

    private async Task<List<T>> GetCatalogueAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await SendAsync(HttpMethod.Get, relativeUrl, ifNoneMatch: null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw new CtmsOfflineException($"The CTMS catalogue '{relativeUrl}' could not be reached.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await ToApiExceptionAsync(response, cancellationToken).ConfigureAwait(false);
            }

            return await ReadJsonAsync<List<T>>(response, cancellationToken).ConfigureAwait(false) ?? new List<T>();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativeUrl, string? ifNoneMatch, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrEmpty(ifNoneMatch))
        {
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue($"\"{ifNoneMatch}\""));
        }

        if (request.Headers.Authorization is null)
        {
            var token = await ResolveTokenAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
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

    private StoredTranslations ToStored(TranslationsWire? wire, HttpResponseMessage response, string requestedLanguage, DateTimeOffset retrievedAt, DateTimeOffset lastValidatedAt)
    {
        if (wire is null)
        {
            throw new CtmsApiException(200, "Malformed response", "The CTMS API returned an empty translations body.");
        }

        return new StoredTranslations
        {
            Application = string.IsNullOrEmpty(wire.Project) ? _options.Application : wire.Project,
            Language = string.IsNullOrEmpty(wire.Language) ? requestedLanguage.Trim() : wire.Language,
            Entries = new Dictionary<string, string>(wire.Translations, StringComparer.Ordinal),
            Etag = ReadETag(response),
            RetrievedAt = retrievedAt,
            LastValidatedAt = lastValidatedAt,
        };
    }

    private void Log(string message) => _options.DiagnosticsLogger?.Invoke("[CTMS.Client] " + message);

    private static string ReadETag(HttpResponseMessage response)
    {
        var tag = response.Headers.ETag?.Tag;
        if (string.IsNullOrEmpty(tag))
        {
            return string.Empty;
        }

        tag = tag!.Trim();
        if (tag.StartsWith("W/", StringComparison.Ordinal))
        {
            tag = tag.Substring(2).Trim();
        }

        if (tag.Length >= 2 && tag[0] == '"' && tag[tag.Length - 1] == '"')
        {
            tag = tag.Substring(1, tag.Length - 2);
        }

        return tag;
    }

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

    private string TranslationsPath(string language) =>
        $"api/translations/{Uri.EscapeDataString(_options.Application.Trim())}/{Uri.EscapeDataString(language.Trim())}";

    private static void RequireLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException("A language code is required.", nameof(language));
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
