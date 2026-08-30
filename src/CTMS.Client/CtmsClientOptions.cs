using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CTMS.Client.Caching;

namespace CTMS.Client;

/// <summary>
/// Configuration for <see cref="CtmsClient"/>. Only <see cref="BaseAddress"/> (or an injected
/// <see cref="HttpClient"/> that carries one) and <see cref="Application"/> are required.
/// </summary>
public sealed class CtmsClientOptions
{
    /// <summary>
    /// Root address of the CTMS API, e.g. <c>http://localhost:8080</c> (container) or
    /// <c>http://localhost:5147</c> (dev). A path segment is preserved and a trailing slash is
    /// added automatically. Ignored when <see cref="HttpClient"/> already has a
    /// <see cref="System.Net.Http.HttpClient.BaseAddress"/>.
    /// </summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// The application <b>code</b> (the <c>Project</c> slug, e.g. <c>nimbus</c>) whose published
    /// translations this client fetches. Required; an empty value throws at construction.
    /// </summary>
    public string Application { get; set; } = string.Empty;

    /// <summary>
    /// Language used as the last link of the client-side fallback chain before the key itself
    /// (see <see cref="ICtmsClient.Get(string, string, string[])"/>). Optional. The API already
    /// fills gaps server-side from each language's <c>FallbackCode</c> chain; this is a secondary
    /// safety net for keys resolved across several prefetched languages.
    /// </summary>
    public string? DefaultLanguage { get; set; }

    /// <summary>
    /// Static bearer token / API key sent as <c>Authorization: Bearer &lt;token&gt;</c>. Only needed
    /// when the deployment sets <c>Auth:PublicBundleReads=false</c>. <see cref="AuthTokenProvider"/>
    /// takes precedence when both are set.
    /// </summary>
    public string? AuthToken { get; set; }

    /// <summary>
    /// Async delegate that returns a fresh bearer token per request (e.g. from MSAL). Return
    /// <c>null</c>/empty to send no <c>Authorization</c> header. Takes precedence over
    /// <see cref="AuthToken"/>.
    /// </summary>
    public Func<CancellationToken, Task<string?>>? AuthTokenProvider { get; set; }

    /// <summary>
    /// Pre-built <see cref="System.Net.Http.HttpClient"/> to use. When null the SDK creates one
    /// from <see cref="BaseAddress"/> and <see cref="RequestTimeout"/> and owns its lifetime.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>
    /// Directory for the on-disk translation cache. When set (and <see cref="TranslationStore"/> is
    /// null) a <see cref="FileTranslationStore"/> rooted here is used; when null the cache is
    /// in-memory only.
    /// </summary>
    public string? CacheDirectory { get; set; }

    /// <summary>
    /// Explicit cache implementation. Overrides <see cref="CacheDirectory"/> when set.
    /// </summary>
    public ITranslationStore? TranslationStore { get; set; }

    /// <summary>
    /// How long a cached translation set is served without contacting the API. Within this window
    /// <see cref="ICtmsClient.GetTranslationsAsync(string, CancellationToken)"/> returns the cached
    /// copy directly; past it, the call revalidates with <c>If-None-Match</c>. Default
    /// <see cref="TimeSpan.Zero"/> (always revalidate).
    /// </summary>
    public TimeSpan StalenessTtl { get; set; } = TimeSpan.Zero;

    /// <summary>Per-request timeout applied on top of the caller's <see cref="CancellationToken"/>.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Optional last-resort mapping for a key that no language in the chain resolves, used by the
    /// non-nullable <see cref="ICtmsClient.Get(string, string, string[])"/>. When null (or it
    /// returns null) the key itself is returned.
    /// </summary>
    public Func<string, string?>? MissingKeyFallback { get; set; }

    /// <summary>Optional sink for diagnostic lines (offline fallbacks, revalidation outcomes).</summary>
    public Action<string>? DiagnosticsLogger { get; set; }
}
