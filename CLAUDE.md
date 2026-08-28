# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

CTMS — Centralised Translation Management System. A .NET 10 / C# backend service.

## Commands

All commands run from the repository root.

- Build: `dotnet build CTMS.sln` (warnings are errors — the build must stay clean).
- Run the API: `dotnet run --project src/CTMS.Api` (Swagger UI at `/swagger` in Development).
- Test: `dotnet test`
- Run a single test: `dotnet test --filter "FullyQualifiedName~ProjectServiceTests.CreateAsync_rejects_a_duplicate_slug"`
  (or `--filter "DisplayName~duplicate slug"`).
- Restore local tools (first checkout): `dotnet tool restore` (installs `dotnet-ef`).

### EF Core migrations

The `dotnet-ef` tool is pinned in `.config/dotnet-tools.json`. Add a migration with:

```
dotnet ef migrations add <Name> --project src/CTMS.Infrastructure --startup-project src/CTMS.Api --output-dir Persistence/Migrations
```

Migrations live in `src/CTMS.Infrastructure/Persistence/Migrations`. `InitialCreate` is the
baseline. Do not run `dotnet ef database update` here — schema is applied by whoever owns
the target database. A design-time factory (`CtmsDbContextFactory`) supplies a dummy
connection string so migration commands never need a live database.

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
  `CreateProjectRequest`), and the ports it needs: `IProjectRepository`, `IUnitOfWork`.
  DTOs — never entities — cross the API boundary. `AddApplication()` registers services.
- **CTMS.Infrastructure** — EF Core. `CtmsDbContext` with one `IEntityTypeConfiguration<T>`
  per entity under `Persistence/Configurations`, repository implementations under
  `Persistence/Repositories`, and migrations. `CtmsDbContext` implements `IUnitOfWork` and
  stamps timestamps in `SaveChanges`. `AddInfrastructure(IConfiguration)` wires the context
  (Npgsql), the unit of work, and repositories.
- **CTMS.Api** — ASP.NET Core minimal-API host. Composition root only: it references
  Infrastructure solely to call `AddInfrastructure`. Endpoints are grouped in
  `Endpoints/ProjectEndpoints.cs`; errors become RFC 7807 ProblemDetails via
  `ApplicationExceptionHandler`. There is no auth yet — look for `// TODO: auth` markers in
  `Program.cs` and `ProjectEndpoints.cs`.

### Seed data model

- `Project` — Id, Name, Slug (unique), Description?, BaseLocaleCode, CreatedAt, UpdatedAt.
- `Locale` — Id, ProjectId, Code (BCP-47), DisplayName, IsRtl. Unique `(ProjectId, Code)`.
- `TranslationKey` — Id, ProjectId, KeyName (dotted path), Description?. Unique `(ProjectId, KeyName)`.
- `TranslationString` — Id, TranslationKeyId, LocaleId, Value, ReviewState (`Draft` /
  `NeedsReview` / `Approved`, stored as text), UpdatedBy, CreatedAt, UpdatedAt, plus a
  `uint Version` optimistic-concurrency token mapped to PostgreSQL's `xmin` system column.
  Unique `(TranslationKeyId, LocaleId)`.

### Persistence

The store is **PostgreSQL** (`Npgsql.EntityFrameworkCore.PostgreSQL`). The connection
string is configuration key `ConnectionStrings:CtmsDatabase`; override it in any environment
with `ConnectionStrings__CtmsDatabase`. No credentials are committed — `appsettings.json`
ships a passwordless localhost placeholder.

### API surface

Each `/api/*` group carries a `// TODO: auth` marker where `RequireAuthorization()` will go.
Known application/domain exceptions become RFC 7807 ProblemDetails in
`ApplicationExceptionHandler`: `ValidationException`→400, `NotFoundException`→404,
`SlugAlreadyInUseException`/`ConflictException`/`ConcurrencyException`/
`InvalidReviewTransitionException`→409 (plus EF `DbUpdateConcurrencyException`→409).

**Health**

- `GET /health` — liveness (no checks).
- `GET /health/ready` — readiness; runs an EF Core `CanConnect` check (tag `ready`).

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
  draft). If `expectedVersion` is supplied and does not match the stored `Version`, the
  response is `409` with `extensions.currentVersion`; an EF `DbUpdateConcurrencyException`
  maps to the same `409`.

**Review workflow**

- `POST /api/projects/{projectId:guid}/keys/{keyId:guid}/strings/{localeId:guid}/review` —
  body `{ "action": "submit" | "approve" | "reject" | "reopen", "reviewedBy": "..." }`;
  `200` with `TranslationStringDto`, `404` if the string does not exist, `409`
  (`InvalidReviewTransitionException`) for an illegal transition. The transition rules live on
  the `TranslationString.ChangeReviewState` domain method:

  | action  | from        | to          |
  |---------|-------------|-------------|
  | submit  | Draft       | NeedsReview |
  | approve | NeedsReview | Approved    |
  | reject  | NeedsReview | Draft       |
  | reopen  | Approved    | NeedsReview |

  Any other `(from, to)` pair throws `InvalidReviewTransitionException`. A successful
  transition sets `UpdatedBy` to `reviewedBy`; PostgreSQL's `xmin` advances the `Version`.

### Tests

`tests/CTMS.Application.Tests` (xUnit) exercises the application services against a real
`CtmsDbContext` on SQLite in-memory (a kept-open `:memory:` connection per test class), plus
`ReviewWorkflowTests` which drives the `TranslationString` review transitions directly.
