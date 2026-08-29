# CTMS — Centralised Translation Management System

[![CI](https://github.com/mohammedmsadiq/CTMS/actions/workflows/ci.yml/badge.svg)](https://github.com/mohammedmsadiq/CTMS/actions/workflows/ci.yml)

A .NET 10 backend that stores translations, walks them through a
draft → review → approve → publish workflow, and serves immutable versioned
bundles with ETag/`304` revalidation. Ships with a Blazor admin site and a
client SDK built on the same HTTP surface.

## Run it locally

```
docker compose up -d --build          # MongoDB + Redis + API on :8080, seeded, auth off
dotnet run --project src/CTMS.Api     # or run the API directly
```

Swagger is at `http://localhost:8080/swagger` in Development.

## Layout

| Path | What |
|------|------|
| `src/CTMS.Api` | ASP.NET Core minimal-API host — endpoints, auth policies, health |
| `src/CTMS.Application` | use-case services, DTOs, repository ports |
| `src/CTMS.Infrastructure` | MongoDB repositories, index/seed startup, Redis bundle cache |
| `src/CTMS.Domain` | entities and lifecycle rules, no framework dependencies |
| `src/CTMS.AdminUI` | Blazor admin dashboard |
| `src/CTMS.Client` | client SDK for consuming published bundles at runtime |
| `tests/` | application, HTTP integration, and client-SDK test suites |
| `deploy/` | Dockerfile is at the repo root; `deploy/azure` holds the Bicep |

## Documentation

- [`CLAUDE.md`](CLAUDE.md) — build/test/run commands and the architecture summary
- [`docs/architecture.md`](docs/architecture.md) — the big picture
- [`docs/api.md`](docs/api.md) — the REST reference
- [`docs/client-sdk.md`](docs/client-sdk.md) — the SDK
- [`docs/development.md`](docs/development.md) — local setup
- [`docs/adr/`](docs/adr/) — architecture decision records

## Test

```
dotnet test
```

## Licence

MIT — see [`LICENSE`](LICENSE).
