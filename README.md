# CTMS — Central Translation Management Service

[![CI](https://github.com/mohammedmsadiq/CTMS/actions/workflows/ci.yml/badge.svg)](https://github.com/mohammedmsadiq/CTMS/actions/workflows/ci.yml)

A .NET 10 service that is the **single source of truth for translations** across
the organisation. It stores translation strings for many **projects** and
**languages**, runs them through a `Draft → InReview → Approved → Published`
workflow, and serves **assembled-on-demand** published translations with
`ETag` / `304` revalidation.

**One engine, two consumption paths:**

- **Internal .NET microservices** call `ITranslationService.GetTranslationsAsync`
  in-process — no HTTP, no direct MongoDB/Redis access.
- **External apps** (MAUI, websites, React/Angular, other services) call
  `GET /api/translations/{project}/{language}`.

Both run the same Application logic and return the same result. MongoDB is the
source of truth; Redis is a cache. Ships with a Blazor admin site
(`CTMS.AdminUI`) and an optional client library (`CTMS.Client`).

## Run it locally

```bash
docker compose up -d --build          # MongoDB + Redis + API on :8080, seeded, auth off
# or run the API directly (needs a reachable MongoDB):
dotnet run --project src/CTMS.Api
dotnet run --project src/CTMS.AdminUI  # the admin dashboard
```

Swagger is at `http://localhost:8080/swagger` (container) or
`http://localhost:5147/swagger` (`dotnet run`) in Development. Health:
`/health`, `/health/live`, `/health/ready`.

```bash
dotnet test        # ~287 cases across three xUnit projects; no Docker required
```

## Layout

| Path | What |
|---|---|
| `src/CTMS.Api` | ASP.NET Core minimal-API host — endpoints, auth policies, health, hardening |
| `src/CTMS.Application` | use-case services, DTOs, repository ports; the `ITranslationService` engine |
| `src/CTMS.Infrastructure` | MongoDB repositories, index/seed startup, the Redis/in-memory delivery cache; `AddTranslationServices` |
| `src/CTMS.Domain` | entities and lifecycle rules, no framework dependencies |
| `src/CTMS.AdminUI` | Blazor admin dashboard (Entra ID OIDC) |
| `src/CTMS.Client` | optional NuGet client library for the REST API |
| `tests/` | application, HTTP integration, and client-library test suites |
| `deploy/` | `deploy/azure` holds the Bicep; the `Dockerfile` is at the repo root |

## Documentation

- [`CLAUDE.md`](CLAUDE.md) — the product specification and the build/test/run +
  architecture cheat-sheet
- [`docs/`](docs/README.md) — the full reference set. Start with:
  - [`docs/architecture.md`](docs/architecture.md) — the big picture
  - [`docs/api.md`](docs/api.md) — the REST reference
  - [`docs/internal-consumption.md`](docs/internal-consumption.md) /
    [`docs/external-consumption.md`](docs/external-consumption.md) — the two
    consumption paths
  - [`docs/local-development.md`](docs/local-development.md) — local setup
  - [`docs/adr/`](docs/adr/) — architecture decision records

## Licence

MIT — see [`LICENSE`](LICENSE).
