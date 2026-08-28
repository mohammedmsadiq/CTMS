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

### API surface (first vertical slice)

- `GET /health` — liveness (no checks).
- `GET /health/ready` — readiness; runs an EF Core `CanConnect` check (tag `ready`).
- `GET /api/projects` — list `ProjectDto`.
- `POST /api/projects` — body `CreateProjectRequest` (`name`, `baseLocaleCode`, optional
  `slug`, optional `description`); `201` with `ProjectDto`; `409` if the slug is taken;
  `400` on validation failure.
- `GET /api/projects/{id:guid}` — `ProjectDto` or `404`.

### Tests

`tests/CTMS.Application.Tests` (xUnit) exercises `ProjectService` against a real
`CtmsDbContext` on SQLite in-memory (a kept-open `:memory:` connection per test class).
