# CTMS documentation

Deeper reference for the Centralised Translation Management System. The
repository-root [`CLAUDE.md`](../CLAUDE.md) is the short, always-current
command + architecture cheat-sheet; these documents expand on it and should not
duplicate it.

| Document | What it covers |
|----------|----------------|
| [architecture.md](architecture.md) | Project layering and dependency flow, domain aggregates, the translation lifecycle state machine, publishing / immutable bundles, MongoDB persistence, Redis caching, configuration and secrets. |
| [api.md](api.md) | REST reference for every route under `/health` and `/api/*`: methods, request/response DTO shapes, status codes, and the exception -> RFC 7807 ProblemDetails mapping. Includes a "planned" section for the bundle and history endpoints. |
| [development.md](development.md) | Local setup: prerequisites, running MongoDB + Redis with Docker Compose, running the API, running tests, the dev seeder toggle, how indexes are created. |
| [adr/](adr/) | Architecture Decision Records (Nygard format). |

## Architecture Decision Records

| ADR | Title | Status |
|-----|-------|--------|
| [0001](adr/0001-record-architecture-decisions.md) | Record architecture decisions | Accepted |
| [0002](adr/0002-mongodb-as-primary-store.md) | MongoDB as the primary datastore | Accepted |

## Status legend

The backend's persistence layer has moved from PostgreSQL/EF Core to MongoDB;
the bundle-publish/delivery endpoints, the audit/history endpoint and the Redis
cache are still to come. Throughout these docs:

- **Implemented** - present in the code on the current branch.
- **Planned** - described here as the target design; not yet in the code (or
  only partially wired).

Where `CLAUDE.md` and these documents disagree on persistence, `CLAUDE.md` has
stale PostgreSQL/EF wording that has not caught up yet; trust the code and these
docs.
