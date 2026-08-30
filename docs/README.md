# CTMS documentation

[![CI](https://github.com/mohammedmsadiq/CTMS/actions/workflows/ci.yml/badge.svg)](https://github.com/mohammedmsadiq/CTMS/actions/workflows/ci.yml)

Deeper reference for the Centralised Translation Management System. The
repository-root [`CLAUDE.md`](../CLAUDE.md) is the short, always-current
command + architecture cheat-sheet; these documents expand on it and should not
duplicate it.

| Document | What it covers |
|----------|----------------|
| [architecture.md](architecture.md) | Project layering and dependency flow, domain aggregates, the translation lifecycle state machine, assemble-on-demand delivery, MongoDB persistence, the Redis-backed delivery cache, bulk import, the API-key / webhook integration surface, configuration and secrets, testing, security. |
| [api.md](api.md) | REST reference for every route under `/health` and `/api/*`: methods, request/response DTO shapes, status codes, the delivery ETag / `If-None-Match` / `304` behaviour, the setup / bulk-operations / integration surface, and the exception -> RFC 7807 ProblemDetails mapping. |
| [client-sdk.md](client-sdk.md) | The `CTMS.Client` SDK: install, `CtmsClientOptions`, the revalidation / offline / stale state machine, the locale fallback chain, the file cache layout, `AddCtmsClient` DI, auth for locked-down deployments, and a MAUI wiring snippet. |
| [development.md](development.md) | Local setup: prerequisites, running MongoDB + Redis with Docker Compose, running the API, the new-application wizard and import screen, running tests, the dev seeder toggle, how indexes are created, CI. |
| [adr/](adr/) | Architecture Decision Records (Nygard format). |

## Architecture Decision Records

| ADR | Title | Status |
|-----|-------|--------|
| [0001](adr/0001-record-architecture-decisions.md) | Record architecture decisions | Accepted |
| [0002](adr/0002-mongodb-as-primary-store.md) | MongoDB as the primary datastore | Accepted |
| [0003](adr/0003-production-hardening.md) | Production hardening: CORS, rate limiting, request-size cap, persistent Data Protection, structured logging | Accepted |
| [0004](adr/0004-assemble-on-demand-delivery-and-model-simplification.md) | Assemble-on-demand delivery and model simplification | Accepted |
| [0005](adr/0005-first-run-experience-and-machine-integration.md) | First-run experience and the machine-integration surface | Accepted |

## Status

Everything documented here is **implemented on the current branch**, with one
exception called out below. The backend runs on MongoDB
([ADR 0002](adr/0002-mongodb-as-primary-store.md)); delivery is assembled on
demand with ETag / `304` + Redis caching
([ADR 0004](adr/0004-assemble-on-demand-delivery-and-model-simplification.md));
production hardening is [ADR 0003](adr/0003-production-hardening.md); the
`CTMS.Client` SDK and its docs are WS6. The first-run helpers from
[ADR 0005](adr/0005-first-run-experience-and-machine-integration.md) — optional
key category with prefix derivation, the language catalogue and bulk register,
bulk file import, bulk review, the publish diff-preview, the API-key auth scheme
(`X-Api-Key`, `/api/api-keys`) and publish webhooks (`/api/webhooks`) — are
**implemented**.
