# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

CTMS — Centralised Translation Management System. A .NET 10 / C# backend service that stores
translation strings for many **applications** and **languages**, runs them through a
review/approval workflow, and serves **assembled-on-demand** published translations to client
applications.

## Commands

All commands run from the repository root.

- Build: `dotnet build CTMS.sln` (warnings are errors — the build must stay clean).
- Run the API: `dotnet run --project src/CTMS.Api` (Swagger UI at `/swagger` in Development).
- Test: `dotnet test`
- Run a single test: `dotnet test --filter "FullyQualifiedName~PublishedTranslationsServiceTests"`
  (or `--filter "DisplayName~fallback chain"`).

### Persistence — MongoDB + Redis

The store is **MongoDB** (`MongoDB.Driver`). Connection string key
`ConnectionStrings:CtmsDatabase`; database name `Mongo:Database` (default `ctms`). Redis
(`ConnectionStrings:Redis`) backs the delivery cache; when unset an in-process
distributed-memory cache is used, so a local `dotnet run` needs no Redis. **There is no
migration tool** — `MongoIndexInitializer` (an `IHostedService`) creates every index on
startup; schema changes are additive and unknown-field-tolerant (`IgnoreExtraElements`). A
one-off backfill command is written by hand when a rewrite is unavoidable.

## Architecture

Four projects under `src/`, plus tests under `tests/`. Dependencies point inward:

```
CTMS.Api  ──►  CTMS.Application  ──►  CTMS.Domain
   │                                     ▲
   └────►  CTMS.Infrastructure  ─────────┘   (also ──► CTMS.Application)
```

- **CTMS.Domain** — entities and domain logic. No framework dependencies. Entities derive from
  `Entity` (Guid `Id`, `CreatedAt`, `UpdatedAt`); constructors/methods guard invariants and
  setters are private. `AuditEntry` is append-only (only `Id` + `Timestamp`).
  `[InternalsVisibleTo("CTMS.Infrastructure")]` lets the persistence layer stamp timestamps.
- **CTMS.Application** — use-case orchestration (`ProjectService`, `LanguageService`,
  `TranslationKeyService`, `TranslationStringService`, `PublishedTranslationsService`,
  `AuditService`), DTOs, and the ports it needs (`IProjectRepository`, `ILanguageRepository`,
  `ITranslationKeyRepository`, `ITranslationStringRepository`, `IAuditRepository`,
  `IPublishedTranslationsCache`, `IUnitOfWork`). DTOs — never entities — cross the API
  boundary. `AddApplication()` registers the services.
- **CTMS.Infrastructure** — MongoDB. `CtmsMongoContext` with one collection per aggregate,
  repository implementations under `Persistence/Repositories`, `PublishedTranslationsCache`
  over `IDistributedCache` under `Persistence/Caching`, and the `MongoIndexInitializer` /
  `DataSeeder` hosted services. `AddInfrastructure(IConfiguration)` wires the Mongo client,
  the (no-op) unit of work, the repositories, the readiness health check, and the cache.
- **CTMS.Api** — ASP.NET Core minimal-API host. Composition root only. Endpoints are grouped
  per resource under `Endpoints/`; errors become RFC 7807 ProblemDetails via
  `ApplicationExceptionHandler`. Entra ID JWT bearer + role/policy authorization, with the
  `Auth:Enabled=false` dev bypass and the `Auth:PublicBundleReads` anonymous-delivery path.

### Data model

- **`Language`** (global, collection `languages`, unique index `code`) — `Code` (BCP-47,
  unique, e.g. `en-GB`), `Name`, `FallbackCode?` (another language code — `fr-CA` → `fr-FR` →
  `en-GB`), `IsRtl`, `Active`.
- **`Project`** = an *application* (collection `projects`, unique index `slug`) — `Name`,
  `Slug` (the application **code** used on client routes), `Description?`, `BaseLanguageCode`,
  `IsShared` (a shared app like `common` whose published translations merge into every app's
  bundle), `Active`, `EnabledLanguageCodes` (`IReadOnlyList<string>`; add/remove validate the
  language exists and is active).
- **`TranslationKey`** (collection `translationKeys`, unique `(projectId, keyName)`,
  non-unique `(projectId, category)`) — `KeyName` (dotted path, `[A-Za-z0-9_.-]+`), `Category`
  (required — `Common`, `Navigation`, `Course`, …), `Description?`, `Active`, `CreatedBy`.
- **`TranslationString`** (collection `translationStrings`, unique
  `(translationKeyId, languageCode)`, plus `(translationKeyId, reviewState, updatedAt desc)`) —
  `LanguageCode` (string), `Value`, `ReviewState` (`Draft` / `NeedsReview` / `Approved` /
  `Published`, stored as text), `UpdatedBy`. **Last write wins** — there is no version token.
- **`AuditEntry`** (collection `auditEntries`, `(projectId, timestamp)` +
  `(entityType, entityId, timestamp)`) — append-only; `Action`, `Actor`, `Timestamp`,
  `FromState?`, `ToState?`, `Detail?`, and value diffs `OldValue?` / `NewValue?` (`NewValue`
  on `Created`; both on `Edited`; null on review transitions).

There are **no versioned bundles** — `TranslationBundle` and `TranslationString.Version` were
removed. Published translations are assembled on demand (see below).

### Assemble-on-demand published translations

`PublishedTranslationsService.GetPublishedAsync(applicationCode, languageCode)`:

1. resolve the application (404 unknown/inactive) and language (404 unknown/inactive, or not
   in the application's `EnabledLanguageCodes`);
2. gather `TranslationString`s with `ReviewState == Published` for **this application's** keys
   plus **every `IsShared` application's** keys. On a key-name collision the **app-specific
   value wins** over a shared one;
3. for keys still missing a published value in `languageCode`, walk the `FallbackCode` chain
   (`fr-CA` → `fr-FR` → `en-GB`, cycle-guarded) and take the first published value found; a
   key with no published value anywhere is omitted;
4. return a flat `Dictionary<string,string>` (`keyName` → `value`), ordered by key.

**Content hash / ETag** — `TranslationContentHash.Compute`: lowercase-hex SHA-256 over the
ordered entries, each emitted as `key\nvalue\n`. No version number anywhere.

**Redis cache** — key `translations:{applicationCode}:{languageCode}` (both lower-cased),
holding the serialized map + its hash. Read-through; in-memory `IDistributedCache` fallback
when `ConnectionStrings:Redis` is unset. Invalidated by `TranslationCacheInvalidator` on
bulk publish and on any per-string review transition that enters or leaves `Published` (or an
edit that knocks a `Published` string back). **Invalidating a shared application fans out
across every application's cache** for the affected languages.

### API surface

Base: `/api`. Each `/api/*` group carries a named authorization policy. `GET /health`,
`GET /health/ready` and `/swagger` are anonymous. The **client delivery reads** are anonymous
while `Auth:PublicBundleReads` is `true` (default) and require `CanRead` otherwise.

Known exceptions → RFC 7807: `ValidationException`→400, `NotFoundException`→404,
`SlugAlreadyInUseException`/`ConflictException`/`InvalidReviewTransitionException`→409.

**Client delivery** (anonymous by default)

- `GET /api/translations/{application}/{language}` → `{ application, language, translations }`.
  Sets `ETag: "<hash>"` and `Cache-Control: no-cache`; honours `If-None-Match` → `304`.
  `404` unknown/inactive application or language, or language not enabled for the app.
- `GET /api/languages?includeInactive=` → `LanguageDto[]` (active only by default).
- `GET /api/applications?includeInactive=` → `ApplicationDto[]` (active only by default).

**Languages** — `GET /api/languages/{code}` (`CanRead`); `POST /api/languages`,
`PATCH /api/languages/{code}` (`CanManageContent`).

**Applications** — `GET /api/applications/{code}` (`CanRead`); `POST /api/applications`
(`CanAdminProjects`); `PATCH /api/applications/{code}`,
`PUT|DELETE /api/applications/{code}/languages/{language}` (`CanManageContent`).
`ApplicationDto { code, name, description?, isShared, active, baseLanguageCode, enabledLanguageCodes }`.

**Translation keys** (nested, `CanRead` / `CanManageContent`)

- `GET /api/applications/{application}/keys?category=&skip=&take=` → `PagedResult<TranslationKeyDto>`
- `GET|POST /api/applications/{application}/keys[/{keyId:guid}]`,
  `PATCH|DELETE .../keys/{keyId:guid}`. Create/Update carry `category`.

**Translation strings** (nested)

- `GET .../keys/{keyId:guid}/strings` and `GET .../keys/{keyId:guid}/strings/{language}` (`CanRead`)
- `PUT .../keys/{keyId:guid}/strings/{language}` — upsert (`CanEditStrings`); `201` created /
  `200` updated. Editing a non-`Draft` string resets it to `NeedsReview`. Last write wins.
- `GET /api/applications/{application}/strings?reviewState=&skip=&take=` →
  `PagedResult<TranslationStringDto>` (`CanRead`).
- `POST .../keys/{keyId:guid}/strings/{language}/review` — `{ action, reviewedBy }`,
  `action` ∈ `submit|approve|reject|reopen|publish` (`CanReview`). Transition table unchanged.

**Management translations** (`CanRead`, except publish `CanPublish`)

- `GET /api/translations?application=&category=&language=&search=&skip=&take=` →
  `PagedResult<TranslationRowDto>`. `TranslationRowDto { keyId, key, category, description?,
  values: { "<lang>": { value, status }, … } }` — one row per key, a cell per enabled
  language; missing languages absent from `values`. `search` matches key name OR any value
  (case-insensitive substring).
- `GET /api/categories?application=` → distinct non-empty categories (`string[]`).
- `GET /api/dashboard?application=` → `{ applicationCount, languageCount, keyCount,
  coverage: [ { languageCode, languageName, translatedCount, totalKeys, percent, missingCount } ],
  totalMissing }`. **"Translated" = a `TranslationString` exists in any non-`Draft` state.**
- `GET /api/translations/missing?application=&language=&skip=&take=` →
  `PagedResult<MissingTranslationDto>` = `{ keyId, key, category, missingLanguages: [...] }`
  (keys with no non-`Draft` value in a target language).
- `POST /api/translations/publish` — `{ application, language? }` → `{ published: <count> }`;
  every `Approved` string for the application (and language, if given) → `Published`, audit
  entries written, cache invalidated (shared-app fan-out).

**History** (`CanRead`) — `GET /api/applications/{application}/history?skip=&take=` →
`PagedResult<AuditEntryDto>`; `GET /api/applications/{application}/keys/{keyId:guid}/strings/{language}/history`
→ `AuditEntryDto[]`. `AuditEntryDto` carries `oldValue` / `newValue`.

**Health** — `GET /health` (liveness), `GET /health/ready` (Mongo `ping`, tag `ready`).

### Review workflow

`POST .../strings/{language}/review` `{ action, reviewedBy }`. Transitions on
`TranslationString.ChangeReviewState`:

| action  | from        | to          |
|---------|-------------|-------------|
| submit  | Draft       | NeedsReview |
| approve | NeedsReview | Approved    |
| reject  | NeedsReview | Draft       |
| reopen  | Approved / Published | NeedsReview |
| publish | Approved    | Published   |

Any other pair throws `InvalidReviewTransitionException` (409). Editing a stored string resets
`ReviewState` to `NeedsReview` unless it is `Draft`.

### Auth

Five Entra app roles (`ctms.admin/manager/reviewer/translator/reader`) → six policies
(`CanRead`, `CanEditStrings`, `CanReview`, `CanManageContent`, `CanPublish`,
`CanAdminProjects`) in `src/CTMS.Api/Auth/AuthorizationPolicies.cs` (mirrored in
`CTMS.AdminUI/Auth`). `updatedBy` / `reviewedBy` body fields are overridden with the token
identity when a real bearer token is present (`TokenActor`).

### Tests

- `tests/CTMS.Application.Tests` (xUnit) — application services against a real `CtmsMongoContext`
  on **EphemeralMongo** (in-process `mongod`, shared via the `"mongo"` collection). Each class
  builds a `CtmsTestHarness`; `Infrastructure/Seed.cs` has direct-to-repo arrange helpers.
- `tests/CTMS.Api.IntegrationTests` — the HTTP surface through `WebApplicationFactory` over the
  real `Program`; `MongoFixture` prefers `Testcontainers.MongoDb` (`mongo:7`) and falls back to
  EphemeralMongo. `Support/ApiHelpers.cs` has request helpers.
