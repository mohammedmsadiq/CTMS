# CTMS client SDK (`CTMS.Client`)

The `CTMS.Client` NuGet package pulls an application's **published translations**
from the CTMS API as a flat `key → value` map, caches it locally, revalidates it
cheaply with an `ETag`, keeps serving the last good copy when the API is
unreachable, and resolves keys through a small language fallback chain.

Source of truth: [`src/CTMS.Client`](../src/CTMS.Client) and
[`tests/CTMS.Client.Tests`](../tests/CTMS.Client.Tests). This document describes
the public surface only.

- Target frameworks: **`netstandard2.0`** (MAUI / Xamarin / older runtimes) and
  **`net10.0`**. On `netstandard2.0` there is no `IServiceCollection` extension —
  construct `CtmsClient` directly.
- The SDK only reads: `GET /api/translations/{application}/{language}` plus the
  `GET /api/languages` / `GET /api/applications` catalogues. It never writes.
  Those routes are anonymous while the API runs with
  `Auth:PublicBundleReads=true` (the default).
- **There are no version numbers.** A publish changes the content hash (`ETag`);
  the SDK notices via `If-None-Match` and re-downloads. Server-side fallback
  (`Language.FallbackCode`) already fills gaps before the map is returned, so the
  client asks for exactly one language.

---

## Install

```bash
dotnet add package CTMS.Client
```

(Published by the release pipeline; `CTMS.Client.csproj` sets `IsPackable`,
`PackageId = CTMS.Client`.)

---

## Quick start

```csharp
using CTMS.Client;

var client = new CtmsClient(new CtmsClientOptions
{
    BaseAddress     = new Uri("https://ctms.example.com"),
    Application     = "icoach",                 // the application code
    DefaultLanguage = "en-GB",
    CacheDirectory  = "/var/cache/ctms",        // omit for in-memory only
    StalenessTtl    = TimeSpan.FromMinutes(5),  // serve cache for 5 min before revalidating
});

// Load (and cache) the languages you need, then resolve synchronously.
await client.PrefetchAsync(new[] { "fr-CA", "fr-FR", "en-GB" });

string label = client.Get("checkout.button.submit", "fr-CA", "fr-FR"); // -> "Payer"
```

`CtmsClient` is thread-safe. Construct one per application and reuse it (register
it as a singleton).

---

## `CtmsClientOptions`

| Property | Type | Default | Meaning |
|----------|------|---------|---------|
| `BaseAddress` | `Uri?` | – | Root address of the CTMS API. A trailing slash is added automatically. Ignored when `HttpClient` already has a `BaseAddress`. **Required** unless an `HttpClient` with a base address is supplied. |
| `Application` | `string?` | – | **Required.** The application **code** whose translations this client fetches. Blank throws at construction. |
| `DefaultLanguage` | `string?` | `null` | Last link of the client-side fallback chain before `MissingKeyFallback` / the key itself. |
| `AuthToken` | `string?` | `null` | Static bearer token sent as `Authorization: Bearer <token>`. Only needed when the deployment sets `Auth:PublicBundleReads=false`. |
| `AuthTokenProvider` | `Func<CancellationToken, Task<string?>>?` | `null` | Async per-request token source (e.g. MSAL). **Takes precedence over `AuthToken`.** Return `null`/empty to send no header. |
| `HttpClient` | `HttpClient?` | `null` | Pre-built client to use. When `null` the SDK creates and owns one from `BaseAddress` + `RequestTimeout`. |
| `CacheDirectory` | `string?` | `null` | Root directory for the on-disk cache. When set (and `TranslationStore` is `null`) a `FileTranslationStore` is rooted here; otherwise the cache is in-memory only. |
| `TranslationStore` | `ITranslationStore?` | `null` | Explicit cache implementation. Overrides `CacheDirectory`. |
| `StalenessTtl` | `TimeSpan` | `TimeSpan.Zero` | How long a cached set is served without contacting the API. `Zero` = always revalidate. |
| `RequestTimeout` | `TimeSpan` | `30s` | Per-request timeout, layered on the caller's token. `Zero` disables it. |
| `MissingKeyFallback` | `Func<string, string?>?` | `null` | Last-resort mapping for a key the chain does not resolve, used by the non-nullable `Get` overload. When `null` (or it returns `null`) the key itself is returned. |
| `DiagnosticsLogger` | `Action<string>?` | `null` | Sink for diagnostic lines (offline fallbacks, revalidation outcomes). Lines are prefixed `[CTMS.Client]`. |

---

## `ICtmsClient`

```csharp
Task<TranslationSet>                 GetTranslationsAsync(string language, CancellationToken ct = default);
Task                                PrefetchAsync(IEnumerable<string> languages, CancellationToken ct = default);
Task<IReadOnlyList<LanguageInfo>>    GetLanguagesAsync(CancellationToken ct = default);
Task<IReadOnlyList<ApplicationInfo>> GetApplicationsAsync(CancellationToken ct = default);
string?                             Get(string key, string language);
string                              Get(string key, string language, params string[] extraFallbackLanguages);
```

### `TranslationSet`

Immutable view handed to callers.

| Member | Notes |
|--------|-------|
| `Application`, `Language` | Exactly as the API returned them. |
| `Entries` | `IReadOnlyDictionary<string,string>`, **ordinal** (case-sensitive) keys, matching the server. Server-side fallback is already applied. |
| `Etag` | Raw lowercase-hex SHA-256 content hash (unquoted). |
| `RetrievedAt` | When the SDK last downloaded the body. |
| `LastValidatedAt` | When the SDK last confirmed the set is current (a fresh `200` or a `304`). Equals `RetrievedAt` until the first revalidation. |
| `IsStale` | `true` only when this copy came from the cache *after the API could not be reached*. A successful fetch or `304` always yields `false`. |
| `TryGetValue(key, out value)` | Direct lookup in this set only (no fallback chain). |

### `LanguageInfo` / `ApplicationInfo`

Thin catalogue models for building a picker. `LanguageInfo` = `{ Code, Name,
FallbackCode?, IsRtl, Active }`; `ApplicationInfo` = `{ Code, Name, Description?,
IsShared, Active, BaseLanguageCode, EnabledLanguageCodes }`. Not cached — each
call hits the API; a transport failure throws `CtmsOfflineException`.

---

## The revalidation / offline / stale state machine

`GetTranslationsAsync(language)`:

1. **Read the cache** for `{application} / {language-lowercased}`.
2. **Within the staleness window** (`StalenessTtl > 0` and
   `now - LastValidatedAt < StalenessTtl`): return the cached copy directly,
   `IsStale = false`, **no network call**.
3. Otherwise **send `GET` with `If-None-Match: "<cached etag>"`** (omitted when
   there is no cache):
   - **Transport failure** (DNS/socket/IO/timeout — a genuine caller
     cancellation is *not* caught):
     - cache present → return it with **`IsStale = true`** and log a line;
     - no cache → throw **`CtmsOfflineException`**.
   - **`304 Not Modified`** → set `LastValidatedAt = now`, persist, return the
     cached copy, `IsStale = false`. (A `304` with no cache is a
     `CtmsApiException`.)
   - **`200 OK`** → parse the flat map, read the `ETag` header (strips `W/` and
     quotes), store it (`RetrievedAt = LastValidatedAt = now`), return it,
     `IsStale = false`.
   - **Any other non-success** → throw **`CtmsApiException`** carrying the HTTP
     status and, when the body is `application/problem+json`, its `title` /
     `detail`. A `404` means the application or language is unknown/inactive, or
     the language is not enabled for the application.

`PrefetchAsync(languages)` warms the cache and the in-memory resolver for each
language. Per-language `CtmsException`s are logged and swallowed — a warm-up
never throws (caller cancellation still propagates).

`Get(...)` is **purely synchronous and in-memory**: it resolves against sets
already materialised by `GetTranslationsAsync` / `PrefetchAsync`. It never
triggers a fetch. Call one of the async methods first.

---

## Language fallback

The **server** does the primary fallback: it walks each language's
`Language.FallbackCode` chain (`fr-CA` → `fr-FR` → `en-GB`) and returns one
already-gap-filled map for the language you requested. In most apps you never
need the client chain.

`Get(...)` keeps a **secondary, in-process** chain across the sets you have
already loaded — useful when you prefetch several languages and want a
belt-and-braces resolve. It is ordered and de-duplicated (case-insensitive):

1. the requested `language` (exact — **no parent-locale expansion**; the server owns that);
2. then each **`extraFallbackLanguages`** from the `params` overload, in order;
3. then the configured **`DefaultLanguage`**;
4. then (non-nullable overload only) **`MissingKeyFallback(key)`**;
5. then the **key itself**.

`Get(key, language)` returns `string?` and stops at step 3 (returns `null` if
unresolved). `Get(key, language, params string[] extraFallbackLanguages)`
returns a guaranteed non-null `string`.

---

## File cache layout

`FileTranslationStore` writes **two files per language**:

```
{CacheDirectory}/{application}/{language}.json        # the flat { "key": "value" } map — directly consumable
{CacheDirectory}/{application}/{language}.meta.json   # { application, language, etag, retrievedAt, lastValidatedAt }
```

- `application` and `language` are lower-cased; invalid filename chars are
  replaced with `_`.
- The data file is written first, then the meta sibling; **each write is atomic**
  — a temp file in the same directory, then a move/replace. A crash mid-write
  leaves the previous pair intact.
- A **miss** is: either file absent, either file unparseable, or an empty `etag`
  in the meta. Never an exception; a cache write failure never breaks the caller.
- `RemoveAsync` deletes both files.

`InMemoryTranslationStore` (the default when `CacheDirectory` is unset) is a
process-lifetime `ConcurrentDictionary` keyed `{app-lower}/{language-lower}`,
cloning on get/set. Provide your own `ITranslationStore` via `TranslationStore`
to back the cache with something else.

---

## Dependency injection (`net10.0`)

```csharp
services.AddCtmsClient(options =>
{
    options.BaseAddress     = new Uri(config["Ctms:BaseUrl"]!);
    options.Application      = config["Ctms:Application"]!;   // e.g. "icoach"
    options.DefaultLanguage  = "en-GB";
    options.CacheDirectory   = Path.Combine(AppContext.BaseDirectory, "ctms-cache");
    options.StalenessTtl     = TimeSpan.FromMinutes(5);
});
```

`AddCtmsClient`:

- registers `ICtmsClient` as a **singleton**;
- registers a named `HttpClient` (`"CTMS.Client"`) via `IHttpClientFactory` —
  unless `options.HttpClient` is set, which wins — with a `CtmsAuthTokenHandler`
  `DelegatingHandler` that adds `Authorization: Bearer` from `AuthTokenProvider`
  (preferred) or `AuthToken` when the request carries none;
- registers the `ITranslationStore` chosen from `TranslationStore` /
  `CacheDirectory` (falling back to `InMemoryTranslationStore`).

Add resilience (Polly) or extra handlers to the named `HttpClient` as usual.

---

## Authentication (locked-down deployments)

The delivery route is anonymous while the API runs with
`Auth:PublicBundleReads=true` (the default). For a fully private deployment
(`Auth:PublicBundleReads=false`) the SDK must present a bearer token that
satisfies `CanRead`, **or** — for a server-side / CI caller — an
[`X-Api-Key`](api.md#api-keys--read-only-machine-access) supplied on the
`HttpClient` directly:

```csharp
// Static bearer token:
options.AuthToken = config["Ctms:ApiToken"];

// Or a fresh token per request (preferred — e.g. MSAL):
options.AuthTokenProvider = async ct =>
{
    var result = await app.AcquireTokenForClient(scopes).ExecuteAsync(ct);
    return result.AccessToken;
};
```

`AuthTokenProvider` takes precedence over `AuthToken`. Returning `null`/empty
sends no `Authorization` header.

---

## MAUI wiring

The MAUI workload was unavailable when this was written, so there is no runnable
MAUI sample — [`samples/Ctms.ConsoleSample`](../samples/Ctms.ConsoleSample) is
the runnable demo, and [`samples/Ctms.MauiSample/README.md`](../samples/Ctms.MauiSample/README.md)
carries the full project scaffold as documented snippets. The essentials:

**`MauiProgram.cs`** — point the file cache at the per-app writable directory:

```csharp
using CTMS.Client;
using CTMS.Client.Caching;

builder.Services.AddCtmsClient(options =>
{
    options.BaseAddress     = new Uri("https://ctms.example.com");
    options.Application      = "icoach";
    options.DefaultLanguage  = "en-GB";
    // Survives app restarts; per-user, per-app sandbox on every platform.
    options.TranslationStore = new FileTranslationStore(
        Path.Combine(FileSystem.AppDataDirectory, "ctms-translations"));
    options.StalenessTtl     = TimeSpan.FromHours(6); // mobile: revalidate sparingly
});
```

**A page** loading a set and showing freshness:

```csharp
var set = await _ctms.GetTranslationsAsync(CultureInfo.CurrentUICulture.Name);

UpdatedLabel.Text    = $"updated {set.RetrievedAt.LocalDateTime:g}";
StaleBanner.IsVisible = set.IsStale;            // "showing an offline copy"
RefreshButton.Clicked += async (_, _) =>
    await _ctms.GetTranslationsAsync(CultureInfo.CurrentUICulture.Name);

string Tr(string key) => _ctms.Get(key, CultureInfo.CurrentUICulture.Name, "en-GB");
```

Call `PrefetchAsync` for the languages you ship at startup so `Get(...)`
resolves offline from the first frame.

---

## Errors

| Exception | When |
|-----------|------|
| `CtmsException` | Base type for everything the SDK throws. |
| `CtmsApiException` | The API returned an error response. Carries `StatusCode`, `Title?`, `Detail?` (parsed from `application/problem+json` when present). |
| `CtmsOfflineException` | A language was requested that is not cached and the API could not be reached. Treat as "translations unavailable" and fall back to your own defaults. |
| `ArgumentException` | Missing `Application` / `BaseAddress`, blank language or key. |
