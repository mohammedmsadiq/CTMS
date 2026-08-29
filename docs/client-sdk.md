# CTMS client SDK (`CTMS.Client`)

The `CTMS.Client` NuGet package pulls **published translation bundles** from the
CTMS API, caches them locally, revalidates them cheaply, keeps serving the last
good copy when the API is unreachable, and resolves keys through a locale
fallback chain.

Source of truth: [`src/CTMS.Client`](../src/CTMS.Client) and
[`tests/CTMS.Client.Tests`](../tests/CTMS.Client.Tests). This document describes
the public surface only.

- Target frameworks: **`netstandard2.0`** (MAUI / Xamarin / older runtimes) and
  **`net10.0`**. On `netstandard2.0` there is no `IServiceCollection` extension —
  construct `CtmsClient` directly.
- The SDK only reads the bundle routes
  (`GET /api/projects/{projectId}/bundles/{localeCode}[...]`), which are
  anonymous by default. It never writes.

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
    BaseAddress   = new Uri("https://ctms.example.com"),
    ProjectId     = Guid.Parse("11111111-1111-1111-1111-111111111111"),
    DefaultLocale = "en",
    CacheDirectory = "/var/cache/ctms",      // omit for in-memory only
    StalenessTtl  = TimeSpan.FromMinutes(5), // serve cache for 5 min before revalidating
});

// Load (and cache) the bundles you need, then resolve synchronously.
await client.PrefetchAsync(new[] { "fr-CA", "fr", "en" });

string label = client.Get("checkout.button.submit", "fr-CA"); // -> "Payer" (falls back fr-CA -> fr -> en)
```

`CtmsClient` is thread-safe. Construct one per project and reuse it (register it
as a singleton).

---

## `CtmsClientOptions`

| Property | Type | Default | Meaning |
|----------|------|---------|---------|
| `BaseAddress` | `Uri?` | – | Root address of the CTMS API. A trailing slash is added automatically. Ignored when `HttpClient` already has a `BaseAddress`. **Required** unless an `HttpClient` with a base address is supplied. |
| `ProjectId` | `Guid` | – | **Required.** The project whose bundles this client fetches. An empty GUID throws at construction. |
| `DefaultLocale` | `string?` | `null` | Last link of the fallback chain before the key itself. |
| `AuthToken` | `string?` | `null` | Static bearer token sent as `Authorization: Bearer <token>`. Only needed when the deployment sets `Auth:PublicBundleReads=false`. |
| `AuthTokenProvider` | `Func<CancellationToken, Task<string?>>?` | `null` | Async per-request token source (e.g. MSAL). **Takes precedence over `AuthToken`.** Return `null`/empty to send no header. |
| `HttpClient` | `HttpClient?` | `null` | Pre-built client to use. When `null` the SDK creates and owns one from `BaseAddress` + `RequestTimeout`. |
| `CacheDirectory` | `string?` | `null` | Root directory for the on-disk cache. When set (and `BundleStore` is `null`) a `FileBundleStore` is rooted here; otherwise the cache is in-memory only. |
| `BundleStore` | `IBundleStore?` | `null` | Explicit cache implementation. Overrides `CacheDirectory`. |
| `StalenessTtl` | `TimeSpan` | `TimeSpan.Zero` | How long a cached *latest* bundle is served without contacting the API. `Zero` = always revalidate. Pinned-version fetches ignore this. |
| `RequestTimeout` | `TimeSpan` | `30s` | Per-request timeout, enforced with a linked cancellation token on top of the caller's token. `Zero` disables it. |
| `MissingKeyFallback` | `Func<string, string?>?` | `null` | Last-resort mapping for a key the chain does not resolve, used by the non-nullable `Get` overload. When `null` (or it returns `null`) the key itself is returned. |
| `DiagnosticsLogger` | `Action<string>?` | `null` | Sink for diagnostic lines (offline fallbacks, revalidation outcomes). Lines are prefixed `[CTMS.Client]`. |

---

## `ICtmsClient`

```csharp
Task<TranslationBundle>            GetBundleAsync(string locale, CancellationToken ct = default);
Task<TranslationBundle>            GetBundleAsync(string locale, int version, CancellationToken ct = default);
Task<IReadOnlyList<BundleVersion>> GetVersionsAsync(string locale, CancellationToken ct = default);
Task                              PrefetchAsync(IEnumerable<string> locales, CancellationToken ct = default);
string?                           Get(string key, string locale);
string                            Get(string key, string locale, params string[] fallbackLocales);
```

### `TranslationBundle`

Immutable view handed to callers.

| Member | Notes |
|--------|-------|
| `ProjectId`, `LocaleCode`, `Version` | `LocaleCode` is exactly as the API returned it; `Version` is the monotonic publish number (starts at 1). |
| `Entries` | `IReadOnlyDictionary<string,string>`, **ordinal** (case-sensitive) keys, matching the server. |
| `Etag` | Raw lowercase-hex SHA-256 content hash (unquoted). |
| `CreatedBy?`, `CreatedAt?` | Populated when the API supplied them. |
| `RetrievedAt` | When the SDK last downloaded the body. |
| `LastValidatedAt` | When the SDK last confirmed the bundle is current (a fresh `200` or a `304`). Equals `RetrievedAt` until the first revalidation. |
| `IsStale` | `true` only when this copy came from the cache *after the API could not be reached*. A successful fetch or `304` always yields `false`. |
| `TryGetValue(key, out value)` | Direct lookup in this bundle only (no fallback chain). |

### `BundleVersion`

One entry from `GET .../bundles/{locale}/versions` (no entries payload):
`Version`, `Etag`, `CreatedAt`, `CreatedBy`, `EntryCount`. Feed `Version` to
`GetBundleAsync(locale, version)` to pin.

---

## The revalidation / offline / stale state machine

`GetBundleAsync(locale)` — the latest bundle:

1. **Read the cache** for `{projectId} / {locale-lowercased}`.
2. **Within the staleness window** (`now - LastValidatedAt < StalenessTtl`):
   return the cached copy directly, `IsStale = false`, **no network call**.
3. Otherwise **send `GET` with `If-None-Match: "<cached etag>"`** (omitted when
   there is no cache):
   - **Transport failure** (DNS/socket/IO/timeout — a genuine caller
     cancellation is *not* caught):
     - cache present → return it with **`IsStale = true`** and log a line;
     - no cache → throw **`CtmsOfflineException`**.
   - **`304 Not Modified`** → set `LastValidatedAt = now`, persist, return the
     cached copy, `IsStale = false`. (A `304` with no cache is a
     `CtmsApiException`.)
   - **`200 OK`** → parse the body, store it (`RetrievedAt = LastValidatedAt =
     now`), return it, `IsStale = false`.
   - **Any other non-success** → throw **`CtmsApiException`** carrying the HTTP
     status and, when the body is `application/problem+json`, its `title` /
     `detail`.

`GetBundleAsync(locale, version)` — a pinned immutable version:

- Cache key `{locale-lowercased}.v{version}`; a cache hit is returned
  immediately and **never revalidated** (no `If-None-Match`).
- On a miss it fetches `.../versions/{version}`; a transport failure with no
  cache throws `CtmsOfflineException`. `version < 1` throws
  `ArgumentOutOfRangeException`.

`PrefetchAsync(locales)` warms the cache and the in-memory resolver for each
locale. Per-locale `CtmsException`s are logged and swallowed — a warm-up never
throws (caller cancellation still propagates).

`Get(...)` is **purely synchronous and in-memory**: it resolves against bundles
already materialised by `GetBundleAsync` / `PrefetchAsync`. It never triggers a
fetch. Call one of the async methods first.

---

## Locale fallback chain

`Get` walks an ordered, de-duplicated (case-insensitive) chain and returns the
first bundle that contains the key:

1. the requested locale and each **parent** — `zh-Hant-TW` → `zh-Hant` → `zh`;
2. then each **explicit fallback locale** from the `params` overload, expanded
   the same way;
3. then the configured **`DefaultLocale`**, expanded the same way;
4. then (non-nullable overload only) **`MissingKeyFallback(key)`**;
5. then the **key itself**.

`Get(key, locale)` returns `string?` and stops at step 3 (returns `null` if
unresolved). `Get(key, locale, params string[] fallbackLocales)` returns a
guaranteed non-null `string`.

---

## File cache layout

`FileBundleStore` writes **one JSON file per bundle**:

```
{CacheDirectory}/{projectId:D}/{cacheKey}.json
```

- `cacheKey` is `fr-ca` for the latest bundle, `fr-ca.v3` for a pinned version
  (locale lower-cased; invalid filename chars replaced with `_`).
- The file is the serialized `StoredBundle` (entries + `etag` + `version` +
  `createdBy`/`createdAt` + `retrievedAt` + `lastValidatedAt`).
- **Writes are atomic:** a temp file in the same directory, then a
  move/replace. A crash mid-write leaves the previous file intact.
- Any missing / unreadable / malformed / partially-written file is treated as a
  **cache miss**, never an exception; a cache write failure never breaks the
  caller.

`InMemoryBundleStore` (the default when `CacheDirectory` is unset) is a
process-lifetime `ConcurrentDictionary`. Provide your own `IBundleStore` via
`BundleStore` to back the cache with something else.

---

## Dependency injection (`net10.0`)

```csharp
services.AddCtmsClient(options =>
{
    options.BaseAddress    = new Uri(config["Ctms:BaseUrl"]!);
    options.ProjectId      = Guid.Parse(config["Ctms:ProjectId"]!);
    options.DefaultLocale  = "en";
    options.CacheDirectory = Path.Combine(AppContext.BaseDirectory, "ctms-cache");
    options.StalenessTtl   = TimeSpan.FromMinutes(5);
});
```

`AddCtmsClient`:

- registers `ICtmsClient` as a **singleton**;
- registers a named `HttpClient` (`CtmsClientServiceCollectionExtensions.HttpClientName`,
  `"CTMS.Client"`) via `IHttpClientFactory` — unless `options.HttpClient` is set,
  which wins;
- registers the `IBundleStore` chosen from `BundleStore` / `CacheDirectory`
  (falling back to `InMemoryBundleStore`).

Add resilience (Polly) or auth handlers to the named `HttpClient` as usual.

---

## Authentication (locked-down deployments)

The bundle GET routes are anonymous while the API runs with
`Auth:PublicBundleReads=true` (the default). For a fully private deployment
(`Auth:PublicBundleReads=false`) the SDK must present a bearer token that
satisfies `CanRead`:

```csharp
// Static token:
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
    options.BaseAddress    = new Uri("https://ctms.example.com");
    options.ProjectId      = Guid.Parse("11111111-1111-1111-1111-111111111111");
    options.DefaultLocale  = "en";
    // Survives app restarts; per-user, per-app sandbox on every platform.
    options.BundleStore    = new FileBundleStore(
        Path.Combine(FileSystem.AppDataDirectory, "ctms-bundles"));
    options.StalenessTtl   = TimeSpan.FromHours(6); // mobile: revalidate sparingly
});
```

**A page** loading a bundle and showing freshness:

```csharp
var bundle = await _ctms.GetBundleAsync(CultureInfo.CurrentUICulture.Name);

VersionLabel.Text  = $"v{bundle.Version}";
RetrievedLabel.Text = $"updated {bundle.RetrievedAt.LocalDateTime:g}";
StaleBanner.IsVisible = bundle.IsStale;          // "showing an offline copy"
RefreshButton.Clicked += async (_, _) =>
    await _ctms.GetBundleAsync(CultureInfo.CurrentUICulture.Name);

string Tr(string key) => _ctms.Get(key, CultureInfo.CurrentUICulture.Name, "en");
```

Call `PrefetchAsync` for the locales you ship at startup so `Get(...)` resolves
offline from the first frame.

---

## Errors

| Exception | When |
|-----------|------|
| `CtmsException` | Base type for everything the SDK throws. |
| `CtmsApiException` | The API returned an error response. Carries `StatusCode`, `Title?`, `Detail?` (parsed from `application/problem+json` when present). |
| `CtmsOfflineException` | A bundle was requested that is not cached and the API could not be reached. Treat as "translations unavailable" and fall back to your own defaults. |
| `ArgumentException` / `ArgumentOutOfRangeException` | Missing `ProjectId` / `BaseAddress`, blank locale or key, `version < 1`. |
