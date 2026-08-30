# External consumption (HTTP)

How an **external application** — a .NET MAUI app, a website, a React or Angular
SPA, an external service — consumes translations from CTMS: one HTTP GET, an
ETag round-trip, and local caching. **The consuming technology must not matter**
(spec §17). External clients never touch MongoDB or Redis.

Internal .NET microservices in the same solution use the in-process path instead
— see [`internal-consumption.md`](internal-consumption.md).

---

## The one route

```
GET /api/translations/{project}/{language}
```

- `{project}` is the project **code** (e.g. `nimbus`); `{language}` is a BCP-47
  code (e.g. `fr-FR`).
- The response is **Common + Project translations in one payload** — the client
  never makes separate calls for common vs project strings, and never resolves a
  language fallback chain itself (the server does both).
- Full contract, headers, and status codes: [`api.md` → Consumer API](api.md#consumer-api).

```json
{
  "project": "nimbus",
  "language": "fr-FR",
  "translations": { "common.save": "Enregistrer", "course.start": "Commencer le cours" }
}
```

There are **no per-key endpoints** and no version numbers (spec §27, §36).

## The ETag round-trip

1. First fetch: `GET /api/translations/nimbus/fr-FR` → `200` with
   `ETag: "abc123"`. Store the body and the ETag.
2. Next fetch: send `If-None-Match: "abc123"`.
   - Unchanged → `304 Not Modified`, no body. Keep using the stored copy.
   - Changed → `200` with a new `ETag` and the new body. Replace the stored copy.

The ETag changes whenever any included common, project, or fallback translation
changes, or a published translation is added or removed. See
[`etag.md`](etag.md).

## Caching guidance

- Honour `Cache-Control: no-cache` — store the body, but revalidate with
  `If-None-Match` before reuse.
- Keep a local copy so the app starts and renders from cache before the network
  responds, and keeps working offline.
- Request **one** language per call. If your UI needs several (e.g. a language
  switcher), fetch and cache each on demand.

## Authentication

The delivery route is **anonymous** while the deployment runs with
`Auth:PublicBundleReads=true` (the default). For a fully private deployment
(`Auth:PublicBundleReads=false`) the client must send an Entra ID bearer token
that satisfies `CanRead`:

```
Authorization: Bearer <access token>
```

See [`authentication.md`](authentication.md). Consuming translations never grants
any management permission (spec §45).

---

## Examples

### `curl`

```bash
# first fetch
curl -i https://ctms.example.com/api/translations/nimbus/fr-FR

# revalidate
curl -i -H 'If-None-Match: "abc123"' https://ctms.example.com/api/translations/nimbus/fr-FR
```

### .NET `HttpClient` (any app — website, service, non-MAUI)

```csharp
using var http = new HttpClient { BaseAddress = new Uri("https://ctms.example.com") };

var request = new HttpRequestMessage(HttpMethod.Get, "/api/translations/nimbus/fr-FR");
if (cachedEtag is not null)
    request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(cachedEtag));  // cachedEtag includes the quotes

using var response = await http.SendAsync(request, ct);

if (response.StatusCode == HttpStatusCode.NotModified)
    return cachedTranslations;                    // 304 — reuse the local copy

response.EnsureSuccessStatusCode();
cachedEtag = response.Headers.ETag?.ToString();   // store for next time
var payload = await response.Content.ReadFromJsonAsync<TranslationsResponse>(ct);
cachedTranslations = payload!.Translations;
return cachedTranslations;

record TranslationsResponse(string Project, string Language, Dictionary<string, string> Translations);
```

### Browser `fetch` (React / Angular / any website)

```js
const res = await fetch(`https://ctms.example.com/api/translations/nimbus/${lang}`, {
  headers: etag ? { "If-None-Match": etag } : {},
});

if (res.status === 304) return cached;          // reuse
const etagHeader = res.headers.get("ETag");
const { translations } = await res.json();
localStorage.setItem(`ctms:nimbus:${lang}`, JSON.stringify({ etag: etagHeader, translations }));
return translations;
```

### .NET MAUI

Use the optional `CTMS.Client` NuGet library — it does the ETag round-trip,
on-disk caching under `FileSystem.AppDataDirectory`, offline-stale fallback, and
a small in-process fallback chain for you. See
[`maui-client.md`](maui-client.md). The library is a **client of this API**; it
is optional and does not replace the service (spec §38).

---

## Websites and other microservices

- **Websites** consume `GET /api/translations/{project}/{language}` and cache the
  bundle. The website does not know how translations are stored (spec §40).
- **Other microservices** that are *not* in the .NET solution use this same HTTP
  route for error messages, notifications, emails, and validation text. A .NET
  microservice that *is* in the solution should use the in-process path instead
  (spec §41). Neither should build its own translation dictionary.
