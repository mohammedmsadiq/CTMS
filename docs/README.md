# CTMS documentation

Deeper reference for the **Central Translation Management Service**. The
repository-root [`CLAUDE.md`](../CLAUDE.md) is the **product specification** and
the always-current command + architecture cheat-sheet; these documents expand on
it and should not contradict it or the code.

**Status legend:** ✅ complete · 🚧 stub / partial.

## The set

| Document | Status | What it covers |
|---|:--:|---|
| [existing-solution-assessment.md](existing-solution-assessment.md) | ✅ | Present-tense assessment of the repo (spec §7): architecture, projects, translation / database / API / Redis / auth architecture, pipelines, tests, dependencies, and the removed features (API keys, webhooks, CSV/RESX import, the language catalogue) with why. |
| [architecture.md](architecture.md) | ✅ | The big picture: one engine (`ITranslationService`), two consumption paths, project layering, the four aggregates, assemble-on-demand delivery, MongoDB + Redis, security, hardening. |
| [database.md](database.md) | ✅ | The five MongoDB collections, every field, the unique + support indexes, the seeder, "no migration tool". |
| [api.md](api.md) | ✅ | REST reference — the **Consumer API** (one ETag-aware route) and the **Management API** (projects, languages, keys, strings, review, review-bulk, publish + preview, import, grid, categories, dashboard, missing, history), status codes, the RFC 7807 mapping, health. |
| [internal-consumption.md](internal-consumption.md) | ✅ | How an internal .NET microservice consumes translations in-process: `AddTranslationServices`, inject `ITranslationService`, `GetTranslationsAsync` → `TranslationBundle`. No HTTP, no Mongo/Redis. |
| [external-consumption.md](external-consumption.md) | ✅ | How external apps consume via `GET /api/translations/{project}/{language}`: the ETag flow, caching, auth, `curl` / `HttpClient` / `fetch` / MAUI examples. |
| [authentication.md](authentication.md) | ✅ | Entra ID / OpenID Connect for management; `Auth:Enabled=false` dev bypass + Production refusal; `Auth:PublicBundleReads`; `AzureAd:*`; the Admin UI OIDC + token-acquisition flow; actor-from-token. |
| [authorisation.md](authorisation.md) | ✅ | The 5 roles, 6 policies, the role → policy map, the spec §46 matrix (and two documented divergences), which endpoint carries which policy. |
| [caching.md](caching.md) | ✅ | Redis as cache-only; key `translations:{project}:{language}`; read-through; in-memory fallback; TTL; invalidation, incl. the `common` fan-out; Redis-outage behaviour. |
| [etag.md](etag.md) | ✅ | The content-hash ETag: SHA-256 over the ordered resolved entries; when it changes; the `If-None-Match` → `304` / `200` flow; no numeric version anywhere. |
| [translation-workflow.md](translation-workflow.md) | ✅ | `Draft → InReview → Approved → Published` (+ `reject`, `reopen`, `archive`/`unarchive`), the transition table, who can do what, "only Published is served", edit semantics, coverage, the audit trail. |
| [local-development.md](local-development.md) | ✅ | Prereqs, `docker compose up`, running the API / Admin UI / tests, the seeder toggle, `Auth:Enabled=false` locally, contributing. |
| [docker.md](docker.md) | ✅ | The root `Dockerfile`, `docker-compose.yml` (dev) and `docker-compose.prod.yml` (prod posture), the env vars, "no production secrets in compose". |
| [azure-deployment.md](azure-deployment.md) | ✅ | The `deploy/azure/main.bicep` resources (ACR, Container Apps, Cosmos-for-Mongo, Redis, Key Vault, Log Analytics, managed identity), the KV secret names, the deploy command, env wiring, known gaps. |
| [azure-devops.md](azure-devops.md) | ✅ | The `azure-pipelines.yml` stage graph, the reusable templates, variable groups, service connections, the four environments + the Production approval gate, "no secrets in YAML", the GitHub Actions PR gate. |
| [maui-client.md](maui-client.md) | ✅ | The optional `CTMS.Client` library — `AddCtmsClient`, `CtmsClientOptions`, `GetTranslationsAsync(language)`, `TranslationSet`, the ETag / `304` / offline-stale state machine, the file cache layout, MAUI wiring. A client of the API; optional; does not replace the service. |
| [migration.md](migration.md) | ✅ | Moving an existing solution's old translation data in: identify → map to `project.key` + categories + languages → import via `POST /api/projects/{project}/import` (json/flat) or a script → validate → retire. "Do not auto-delete production data." |
| [troubleshooting.md](troubleshooting.md) | ✅ | `/health/ready` 503, `401` on management routes, the Production auth refusal, empty / `404` bundles, stale bundle after publish, `dotnet ef` n/a, EphemeralMongo first-run download, port in use, CORS, `429`. |
| [adr/](adr/) | ✅ | Architecture Decision Records + [adr/README.md](adr/README.md) reconciling `0003`–`0005` with the current architecture. |

## Related

- [`../CLAUDE.md`](../CLAUDE.md) — product specification, build/test/run
  commands, architecture summary. **The authority.**
- [`../README.md`](../README.md) — the short project intro.
