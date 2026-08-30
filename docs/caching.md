# Caching

Redis is a **cache only**. MongoDB is the source of truth (spec §5, §29–§30,
§50). The cache holds the fully assembled delivery map so a consumer read costs
no database round-trip and no re-assembly on a hit.

---

## What is cached

Only the consumer delivery result:
`GET /api/translations/{project}/{language}` /
`ITranslationService.GetTranslationsAsync(project, language)`.

The cached value is the serialized `CachedTranslations { translations, hash }` —
the flat `keyName → value` map **already merged** (project + every `common`
project, project value wins) **and fallback-resolved**, plus its content hash.
So an `If-None-Match` / `304` check on a hit needs neither assembly nor a
MongoDB read.

Nothing else is cached — the management grid, dashboard, missing, history, and
all writes go straight to MongoDB.

## Key

```
translations:{projectCode}:{languageCode}
```

Both segments trimmed and lower-cased (`PublishedTranslationsCache.KeyFor`).
Example: `translations:icoach:fr-ca`.

## Backend — Redis or in-process, identical behaviour

`AddInfrastructure` registers an `IDistributedCache`:

| `ConnectionStrings:Redis` | Backend |
|---|---|
| set (`host:port[,options]`, StackExchange.Redis format) | `AddStackExchangeRedisCache` |
| unset | `AddDistributedMemoryCache` — an in-process distributed-memory cache |

`CacheModeLogger` logs which backend is active once at startup. The route
behaves identically either way; a local `dotnet run` needs no Redis.

## TTL

`Cache:TranslationsTtlMinutes` (`TranslationsCacheOptions`), default **60**; a
value `<= 0` falls back to 60. Applied as
`AbsoluteExpirationRelativeToNow` on every write.

## Read-through

`PublishedTranslationsService.GetPublishedAsync`:

1. resolve project + language (404 on unknown / inactive / not enabled);
2. `GET translations:{project}:{language}` — on a hit, return `{ map, hash }`
   without assembling;
3. on a miss, assemble (several collection reads, the per-key fallback walk, the
   content hash), `SET` the entry with the TTL, and return it.

## Invalidation

Driven by `TranslationCacheInvalidator.InvalidateAsync(project, languageCodes)`.
Triggered by:

- a **per-string review transition that enters or leaves `Published`**
  (`TranslationStringService.ReviewAsync`);
- an **edit that knocks a `Published` string back to `InReview`** (the string
  upsert);
- **bulk review** (`review-bulk`) — once at the end, for the languages of
  strings that entered or left `Published`;
- **bulk publish** (`POST /api/translations/publish`) — for the affected
  languages;
- **bulk import** — once at the end, if any imported string entered or left
  `Published` (import writes at `Draft` / `InReview` / `Approved`, so this is
  rare).

Each removes `translations:{project}:{language}` for the affected language(s)
only — **unrelated languages are not touched** (spec §30).

### `common` fan-out

A `common` project (`Project.IsCommon == true`) contributes its published
strings to **every** project's delivered map. So invalidating a `common` project
removes `translations:{p}:{lang}` for **every** project `p`
(`ListAsync(includeInactive: true)`) × each affected language. With many
projects this is a burst of invalidations and a wave of cache-miss re-assembly
on the next request per pair.

## Failure behaviour

Every cache call — get, set, invalidate — is wrapped in `try/catch`. A failure
is logged at `Warning` and treated as a **miss**: delivery degrades to on-demand
assembly straight from MongoDB. An unreadable / corrupt cached entry is
discarded the same way.

Consequently there is **no Redis readiness probe** — a Redis outage does not make
`/health/ready` fail (spec §48, §50). If MongoDB is down, that is a `503` and
delivery raises an error rather than returning wrong data.

## Redis also backs Data Protection

When `ConnectionStrings:Redis` is set, the same connection backs the ASP.NET
Core Data Protection key ring (`DataProtection-Keys`) on both the API and the
Admin UI, so replicas share one key ring. This makes Redis load-bearing for
antiforgery / cookie protection in a multi-replica deployment even though it is
not load-bearing for translation delivery. See
[`authentication.md`](authentication.md).

## Configuration summary

| Key | Env override | Default | Meaning |
|---|---|---|---|
| `ConnectionStrings:Redis` | `ConnectionStrings__Redis` | unset | Redis connection string; unset ⇒ in-process cache |
| `Cache:TranslationsTtlMinutes` | `Cache__TranslationsTtlMinutes` | `60` | Cached-map TTL in minutes (`<= 0` ⇒ 60) |

## Related

- [`etag.md`](etag.md) — the content hash stored alongside the map.
- [`architecture.md` §4](architecture.md#4-assemble-on-demand-delivery) — the
  assembly the cache fronts.
- [`translation-workflow.md`](translation-workflow.md) — what "enters or leaves
  `Published`" means.
