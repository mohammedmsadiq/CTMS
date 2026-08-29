# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

CTMS — Centralised Translation Management System. A .NET 10 / C# backend service.

## Commands

All commands run from the repository root.

- Build: `dotnet build CTMS.sln` (warnings are errors — the build must stay clean).
- Run the API: `dotnet run --project src/CTMS.Api` (Swagger UI at `/swagger` in Development).
- Test: `dotnet test` (needs network on first run: `EphemeralMongo` downloads a `mongod`
  binary and caches it under the local app-data directory).
- Run a single test: `dotnet test --filter "FullyQualifiedName~ProjectServiceTests.CreateAsync_rejects_a_duplicate_slug"`
  (or `--filter "DisplayName~duplicate slug"`).

### Indexes and seed data (no migrations)

MongoDB has no schema migrations. Instead:

- **`MongoIndexInitializer`** (`Persistence/Startup`) is an `IHostedService` that calls
  `EnsureIndexesAsync` on startup, creating every unique/support index (see below). It is
  idempotent — `createIndexes` is a no-op when the spec already matches.
- **`DataSeeder`** (`Persistence/Startup`) is an `IHostedService` that inserts one sample
  project ("Marketing Site") only when the environment is Development **and**
  `Seed:Enabled` is `true`. It is idempotent (skips if the sample project already exists).

Both are registered by `AddInfrastructure`. Tests call
`MongoIndexInitializer.EnsureIndexesAsync` directly against a fresh database.

## Architecture

Four projects under `src/`, plus tests under `tests/`. Dependencies point inward:

```
CTMS.Api  ──►  CTMS.Application  ──►  CTMS.Domain
   │                                     ▲
   └────►  CTMS.Infrastructure  ─────────┘   (also ──► CTMS.Application)
```

- **CTMS.Domain** — entities and domain logic. No framework dependencies. Entities derive
  from `Entity` (Guid `Id`, `CreatedAt`, `UpdatedAt`); constructors/methods guard invariants
  and setters are private.
- **CTMS.Application** — use-case orchestration (`ProjectService`), DTOs (`ProjectDto`,
  `CreateProjectRequest`), and the ports it needs: `IProjectRepository`,
  `ITranslationBundleRepository`, `IAuditRepository`, `IUnitOfWork`.
  DTOs — never entities — cross the API boundary. `AddApplication()` registers services.
- **CTMS.Infrastructure** — MongoDB (`MongoDB.Driver`). `IMongoContext` / `CtmsMongoContext`
  wrap `IMongoClient` + `IMongoDatabase` and expose one typed `IMongoCollection<T>` per
  aggregate. BSON class maps and conventions (camelCase elements, enums-as-strings, standard
  `Guid` representation) live in `Persistence/Mongo/MongoMappingRegistration`. Repository
  implementations are under `Persistence/Repositories` and persist each write immediately;
  `IUnitOfWork` is a `NoOpUnitOfWork` (single-document writes are atomic). Repositories stamp
  `CreatedAt`/`UpdatedAt` just before writing (`EntityStamps`). `AddInfrastructure(IConfiguration)`
  registers the client (singleton), the context (singleton), repositories, the readiness
  health check, and the startup index initializer + data seeder.
- **CTMS.Api** — ASP.NET Core minimal-API host. Composition root only: it references
  Infrastructure solely to call `AddInfrastructure`. Endpoints are grouped in
  `Endpoints/ProjectEndpoints.cs`; errors become RFC 7807 ProblemDetails via
  `ApplicationExceptionHandler`. There is no auth yet — look for `// TODO: auth` markers in
  `Program.cs` and `ProjectEndpoints.cs`.

### Data model

Collection names (camelCase BSON elements throughout):

- `projects` — `Project`: Id, Name, Slug (unique), Description?, BaseLocaleCode, CreatedAt, UpdatedAt.
- `locales` — `Locale`: Id, ProjectId, Code (BCP-47), DisplayName, IsRtl. Unique `(ProjectId, Code)`.
- `translationKeys` — `TranslationKey`: Id, ProjectId, KeyName (dotted path), Description?. Unique `(ProjectId, KeyName)`.
- `translationStrings` — `TranslationString`: Id, TranslationKeyId, LocaleId, Value, ReviewState
  (`Draft` / `NeedsReview` / `Approved` / `Published`, stored as text), UpdatedBy, CreatedAt,
  UpdatedAt, plus a plain incrementing `long Version` optimistic-concurrency token that the
  repository bumps on every stored update and guards with a filtered `ReplaceOne`
  (`Eq(Id) & Eq(Version, expected)`); a zero-match result throws `ConcurrencyException`.
  Unique `(TranslationKeyId, LocaleId)`; plus a support index
  `(TranslationKeyId, ReviewState, UpdatedAt desc)` backing the project-wide string list.
- `translationBundles` — `TranslationBundle`: Id, ProjectId, LocaleCode, Version (`int`, from 1),
  Entries (immutable key→value snapshot of every published string), ETag (lowercase-hex SHA-256
  of the ordered entries), CreatedBy, CreatedAt. Unique `(ProjectId, LocaleCode, Version)`.
  Append-only; a new publish creates a new version. `TranslationBundleService.PublishAsync`
  snapshots (never mutates `ReviewState`); the HTTP publish + read endpoints exist (WS3), while
  Redis/ETag response caching on `GET .../bundles/{localeCode}` is WS4 (`// TODO: WS4` marker in
  `TranslationBundleRepository`).
- `auditEntries` — `AuditEntry`: Id, ProjectId, EntityType, EntityId, Action
  (`Created`/`Edited`/`Submitted`/`Approved`/`Rejected`/`Reopened`/`Published`, stored as text),
  Actor, Timestamp, FromState?, ToState?, Detail?. Append-only. Indexes `(ProjectId, Timestamp)`
  and `(EntityType, EntityId, Timestamp)`. `TranslationStringService` appends an entry on every
  upsert and review transition.

### Persistence

The store is **MongoDB** (`MongoDB.Driver` 3.x). The connection string is configuration key
`ConnectionStrings:CtmsDatabase` (override with `ConnectionStrings__CtmsDatabase`); the database
name is configuration key `Mongo:Database`, default `ctms` (override with `Mongo__Database`). No
credentials are committed — `appsettings.json` ships `mongodb://localhost:27017`.

### API surface

Each `/api/*` group carries a `// TODO: auth` marker where `RequireAuthorization()` will go.
Known application/domain exceptions become RFC 7807 ProblemDetails in
`ApplicationExceptionHandler`: `ValidationException`→400, `NotFoundException`→404,
`SlugAlreadyInUseException`/`ConflictException`/`ConcurrencyException`/
`InvalidReviewTransitionException`→409. `ConcurrencyException` carries
`extensions.currentVersion` (the stored `long` version).

**Health**

- `GET /health` — liveness (no checks).
- `GET /health/ready` — readiness; the `MongoHealthCheck` runs `{ ping: 1 }` against the
  database (name "database", tag `ready`).

**Projects**

- `GET /api/projects` — list `ProjectDto`.
- `POST /api/projects` — body `CreateProjectRequest` (`name`, `baseLocaleCode`, optional
  `slug`, optional `description`); `201` with `ProjectDto`; `409` if the slug is taken;
  `400` on validation failure.
- `GET /api/projects/{id:guid}` — `ProjectDto` or `404`.

**Locales** (nested under a project)

- `GET /api/projects/{projectId:guid}/locales` — list `LocaleDto`.
- `POST /api/projects/{projectId:guid}/locales` — body `CreateLocaleRequest` (`code` BCP-47,
  `displayName`, optional `isRtl`); `201` + `Location`; `404` unknown project; `409` if
  `(projectId, code)` exists; `400` on validation. `code` is trimmed and internal whitespace
  collapsed; casing is preserved.
- `GET /api/projects/{projectId:guid}/locales/{localeId:guid}` — `LocaleDto` or `404`.
- `PATCH /api/projects/{projectId:guid}/locales/{localeId:guid}` — body `UpdateLocaleRequest`
  (`displayName?`, `isRtl?`; omitted members unchanged); `200` or `404`.
- `DELETE /api/projects/{projectId:guid}/locales/{localeId:guid}` — `204` or `404`. Cascades
  to the locale's `TranslationString` rows.

**Translation keys** (nested under a project)

- `GET /api/projects/{projectId:guid}/keys?skip=0&take=50` — `PagedResult<TranslationKeyDto>`
  (`{ items, total }`); `skip` floored at 0, `take` defaulted to 50 and capped at 200.
- `POST /api/projects/{projectId:guid}/keys` — body `CreateTranslationKeyRequest` (`keyName`
  matching `[A-Za-z0-9_.-]+`, optional `description`); `201`; `404` unknown project; `409` if
  `(projectId, keyName)` exists; `400` on validation.
- `GET /api/projects/{projectId:guid}/keys/{keyId:guid}` — `TranslationKeyDto` or `404`.
- `PATCH /api/projects/{projectId:guid}/keys/{keyId:guid}` — body `UpdateTranslationKeyRequest`
  (`description`); `200` or `404`.
- `DELETE /api/projects/{projectId:guid}/keys/{keyId:guid}` — `204` or `404`. Cascades to the
  key's `TranslationString` rows.

**Translation strings** (per key, per locale)

- `GET /api/projects/{projectId:guid}/keys/{keyId:guid}/strings` — `TranslationStringDto[]`
  for every locale, or `404` if the key is not in the project.
- `GET /api/projects/{projectId:guid}/keys/{keyId:guid}/strings/{localeId:guid}` —
  `TranslationStringDto` or `404`.
- `PUT /api/projects/{projectId:guid}/keys/{keyId:guid}/strings/{localeId:guid}` — upsert;
  body `UpsertTranslationStringRequest` (`value`, optional `updatedBy`, optional
  `expectedVersion`). `201` + `Location` when the row is created, `200` when it is updated;
  `404` if the key or locale is not in the project; `400` on validation. Editing an existing
  string resets `ReviewState` to `NeedsReview` unless it is currently `Draft` (a draft stays a
  draft) — this includes `Approved` and `Published` strings. If `expectedVersion` is supplied
  and does not match the stored `Version`, the response is `409` with
  `extensions.currentVersion`; a lost race on the repository's filtered update maps to the
  same `409`.

**Review workflow**

- `POST /api/projects/{projectId:guid}/keys/{keyId:guid}/strings/{localeId:guid}/review` —
  body `{ "action": "submit" | "approve" | "reject" | "reopen" | "publish", "reviewedBy": "..." }`;
  `200` with `TranslationStringDto`, `404` if the string does not exist, `409`
  (`InvalidReviewTransitionException`) for an illegal transition. The transition rules live on
  the `TranslationString.ChangeReviewState` domain method:

  | action  | from        | to          |
  |---------|-------------|-------------|
  | submit  | Draft       | NeedsReview |
  | approve | NeedsReview | Approved    |
  | reject  | NeedsReview | Draft       |
  | reopen  | Approved    | NeedsReview |
  | publish | Approved    | Published   |
  | reopen  | Published   | NeedsReview |

  Any other `(from, to)` pair throws `InvalidReviewTransitionException`. A successful
  transition sets `UpdatedBy` to `reviewedBy`, bumps the `long Version`, and appends an
  `AuditEntry`.

**Project-wide string list** (unblocks the Admin UI review queue)

- `GET /api/projects/{projectId:guid}/strings?reviewState=&skip=0&take=50` —
  `PagedResult<TranslationStringDto>` for every string in the project, newest-updated first.
  `reviewState` (optional) filters by exact `ReviewState` name; an unknown/numeric value is
  `400`. `skip` floored at 0, `take` default 50 capped at 200. `404` unknown project. Scoped
  by matching `translationStrings.translationKeyId` against the project's key ids —
  `TranslationString` is not denormalised with a `projectId`.

**Bundles** (nested under a project; immutable published snapshots)

- `POST /api/projects/{projectId:guid}/bundles/{localeCode}` — body optional
  `{ "publishedBy": "..." }` (blank → `"system"`); snapshots the locale's `Published` strings
  into the next `TranslationBundle` version. `201` `TranslationBundleDto` + `Location` (the
  by-version route); `400` blank locale code or nothing published; `404` unknown
  project/locale; `409` on the `(projectId, localeCode, version)` unique-index race.
  **Publishing never changes a string's `ReviewState`** — strings reach `Published` first via
  the review `publish` action. Writes a `Published` `AuditEntry` (`entityType =
  "TranslationBundle"`). `version` is monotonic per `(projectId, localeCode)` from 1; `etag`
  is a content hash of the entries (stable for identical content).
- `GET /api/projects/{projectId:guid}/bundles/{localeCode}` — latest `TranslationBundleDto`
  or `404`. Plain JSON; the `ETag` header / `If-None-Match` / `304` and the Redis cache on
  this route are **WS4**, still to build (the `etag` value is already in the body).
- `GET /api/projects/{projectId:guid}/bundles/{localeCode}/versions` — `BundleVersionDto[]`
  (`version`, `etag`, `createdAt`, `createdBy`, `entryCount`), ascending by `version`.
- `GET /api/projects/{projectId:guid}/bundles/{localeCode}/versions/{version:int}` —
  `TranslationBundleDto` or `404`.

**History / audit trail** (nested under a project)

- `GET /api/projects/{projectId:guid}/history?skip=0&take=50` — `PagedResult<AuditEntryDto>`,
  newest first (`skip` floored at 0, `take` default 50 capped at 200); `404` unknown project.
- `GET /api/projects/{projectId:guid}/keys/{keyId:guid}/strings/{localeId:guid}/history` —
  `AuditEntryDto[]` for that one string, newest first; `404` if the string does not exist.

### Tests

`tests/CTMS.Application.Tests` (xUnit) exercises the application services and repositories
against a real MongoDB. `EphemeralMongo` starts one throwaway `mongod` for the whole run
(shared via `[Collection("mongo")]` / `MongoFixture`); each test gets an isolated database
with every production index applied, wired through `CtmsTestHarness`, and dropped on dispose.
`ReviewWorkflowTests` drives the `TranslationString` review transitions directly (no
database). First run needs network access so `EphemeralMongo` can download and cache a
`mongod` binary.
