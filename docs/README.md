# CTMS documentation

[![CI](https://github.com/mohammedmsadiq/CTMS/actions/workflows/ci.yml/badge.svg)](https://github.com/mohammedmsadiq/CTMS/actions/workflows/ci.yml)

Deeper reference for the Centralised Translation Management System. The
repository-root [`CLAUDE.md`](../CLAUDE.md) is the short, always-current
command + architecture cheat-sheet; these documents expand on it and should not
duplicate it.

| Document | What it covers |
|----------|----------------|
| [architecture.md](architecture.md) | Project layering and dependency flow, domain aggregates, the translation lifecycle state machine, publishing / immutable bundles, MongoDB persistence, the Redis-backed bundle cache, configuration and secrets, testing, security. |
| [api.md](api.md) | REST reference for every route under `/health` and `/api/*`: methods, request/response DTO shapes, status codes, the bundle ETag / `If-None-Match` / `304` behaviour, and the exception -> RFC 7807 ProblemDetails mapping. |
| [client-sdk.md](client-sdk.md) | The `CTMS.Client` SDK: install, `CtmsClientOptions`, the revalidation / offline / stale state machine, the locale fallback chain, the file cache layout, `AddCtmsClient` DI, auth for locked-down deployments, and a MAUI wiring snippet. |
| [development.md](development.md) | Local setup: prerequisites, running MongoDB + Redis with Docker Compose, running the API, running tests, the dev seeder toggle, how indexes are created, CI. |
| [adr/](adr/) | Architecture Decision Records (Nygard format). |

## Architecture Decision Records

| ADR | Title | Status |
|-----|-------|--------|
| [0001](adr/0001-record-architecture-decisions.md) | Record architecture decisions | Accepted |
| [0002](adr/0002-mongodb-as-primary-store.md) | MongoDB as the primary datastore | Accepted |
| [0003](adr/0003-production-hardening.md) | Production hardening: CORS, rate limiting, request-size cap, persistent Data Protection, structured logging | Accepted |

## Status

Everything documented here is **implemented on the current branch**. The backend
runs on MongoDB ([ADR 0002](adr/0002-mongodb-as-primary-store.md)); the
bundle-publish / delivery endpoints and the audit / history endpoints shipped in
WS3, with ETag / `304` + Redis caching added in WS4; the `CTMS.Client` SDK and
its docs are WS6; production hardening is
[ADR 0003](adr/0003-production-hardening.md), landing in the same batch as these
docs.

Where `CLAUDE.md` and these documents disagree on persistence, `CLAUDE.md` has
stale PostgreSQL / EF wording that has not caught up yet; trust the code and
these docs.
