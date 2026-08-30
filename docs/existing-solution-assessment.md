# Existing solution assessment

_Present-tense description of the repository as it stands on the current branch
(after commit `c34515a`, "Align backend to the new spec"). Based on the actual
code, not on plans._ The product specification is [`CLAUDE.md`](../CLAUDE.md).

This document follows the template in `CLAUDE.md` §7.

---

## Existing architecture

CTMS is a **.NET 10 / C#** solution (`CTMS.sln`, `net10.0`, `global.json` pins
SDK `10.0.400`, `Directory.Build.props` sets `TreatWarningsAsErrors=true`). It is
a clean-architecture backend plus a Blazor admin host and an optional client
library.

```
CTMS.Api  ──►  CTMS.Application  ──►  CTMS.Domain
   │                                      ▲
   └────►  CTMS.Infrastructure  ──────────┘   (also ──► CTMS.Application)

CTMS.AdminUI  ──►  (HTTP only)  ──►  CTMS.Api
CTMS.Client   ──►  (HTTP only)  ──►  CTMS.Api
```

Dependencies point inward. `CTMS.Domain` has no framework dependencies;
`CTMS.Application` has no dependency on ASP.NET or on `CTMS.Infrastructure`;
`CTMS.Api` references `CTMS.Infrastructure` only to call `AddInfrastructure`.

### Projects

| Project | Role |
|---|---|
| `src/CTMS.Domain` | Entities and invariants. `Entity` base type (`Guid Id`, `CreatedAt`, `UpdatedAt`). Aggregates: `Project`, `Language`, `TranslationKey`, `TranslationString`, `AuditEntry`. `ReviewState`, `AuditAction`, `InvalidReviewTransitionException`. |
| `src/CTMS.Application` | Use-case services (`ProjectService`, `LanguageService`, `TranslationKeyService`, `TranslationStringService`, `PublishedTranslationsService`, `TranslationService`, `TranslationImportService`, `AuditService`, `TranslationCacheInvalidator`), DTOs, and repository/cache ports. `AddApplication()` registers them. The one translation engine is `ITranslationService` → `TranslationService` → `PublishedTranslationsService`. |
| `src/CTMS.Infrastructure` | MongoDB persistence (`CtmsMongoContext`, five repositories, `MongoMappingRegistration`), the Redis/in-memory delivery cache (`PublishedTranslationsCache`), `MongoHealthCheck`, and two hosted startup services (`MongoIndexInitializer`, `DataSeeder`). `AddInfrastructure(IConfiguration)` wires it; `AddTranslationServices(IConfiguration)` is the one call an internal .NET consumer makes (it composes `AddApplication` + `AddInfrastructure`). |
| `src/CTMS.Api` | ASP.NET Core minimal-API host. Composition root. Endpoint groups under `Endpoints/*`; RFC 7807 error mapping in `Infrastructure/ApplicationExceptionHandler.cs`; production hardening helpers in `Infrastructure/*Setup.cs`; auth in `Auth/*`. |
| `src/CTMS.AdminUI` | Blazor Web App (InteractiveServer). Entra ID OpenID Connect sign-in, calls the management API with a bearer token acquired on-behalf-of the user. Keeps a byte-identical copy of `AuthRoles` / `AuthorizationPolicies`. |
| `src/CTMS.Client` | Optional `netstandard2.0` + `net10.0` NuGet library. An HTTP client of the consumer delivery route with local caching, ETag revalidation, and offline fallback. Not required — see [`maui-client.md`](maui-client.md). |

Test projects: `tests/CTMS.Application.Tests`, `tests/CTMS.Api.IntegrationTests`,
`tests/CTMS.Client.Tests`. Runnable sample: `samples/Ctms.ConsoleSample`
(`samples/Ctms.MauiSample` is documented scaffold only).

## Existing microservices

There are no business microservices in this repository. CTMS is a single
service. The spec's "internal .NET microservice" is any other service in the
wider solution that references `CTMS.Infrastructure` / `CTMS.Application` and
calls `AddTranslationServices` — it consumes `ITranslationService` in-process,
never over HTTP. See [`internal-consumption.md`](internal-consumption.md).

## Existing translation architecture

One engine, two entry points, identical result:

- **In-process** — `ITranslationService.GetTranslationsAsync(project, language, ct)`
  returns a `TranslationBundle` (`Project`, `Language`,
  `IReadOnlyDictionary<string,string> Translations`, `ETag`).
- **HTTP** — `GET /api/translations/{project}/{language}` is a thin adapter that
  calls the same `ITranslationService` and adds ETag / `If-None-Match` / `304`
  handling.

Both run `TranslationService` → `PublishedTranslationsService.GetPublishedAsync`,
which assembles the map on demand (no stored bundles, no version numbers):
resolve project + language → gather `Published` strings for this project's keys
plus every `IsCommon` project's keys (project value wins a key-name collision) →
fill gaps by walking `Language.FallbackCode` (cycle-guarded) → order by key →
content-hash. A Redis read-through cache fronts it. Details:
[`architecture.md`](architecture.md), [`etag.md`](etag.md),
[`caching.md`](caching.md).

## Existing database architecture

**MongoDB** (`MongoDB.Driver`), database name from `Mongo:Database` (default
`ctms`), connection string `ConnectionStrings:CtmsDatabase`. Five collections,
constants on `CtmsMongoContext`:

| Collection | Document | Key indexes (created by `MongoIndexInitializer` on startup) |
|---|---|---|
| `projects` | `Project` | `{ slug: 1 }` unique |
| `languages` | `Language` | `{ code: 1 }` unique |
| `translationKeys` | `TranslationKey` | `{ projectId: 1, keyName: 1 }` unique; `{ projectId: 1, category: 1 }` |
| `translationStrings` | `TranslationString` | `{ translationKeyId: 1, languageCode: 1 }` unique; `{ translationKeyId: 1, reviewState: 1, updatedAt: -1 }` |
| `auditEntries` | `AuditEntry` | `{ projectId: 1, timestamp: 1 }`; `{ entityType: 1, entityId: 1, timestamp: 1 }` |

No migration tool: `createIndexes` is idempotent and runs every startup; schema
evolution is additive and `IgnoreExtraElements` tolerates unknown fields.
`NoOpUnitOfWork` — every write is a single-document atomic operation.
Referential integrity and cascade deletes are enforced in the application
services and repositories, not by the database. Full field list:
[`database.md`](database.md).

## Existing API architecture

Two surfaces on one host (`src/CTMS.Api/Endpoints/*`):

- **Consumer API** — `GET /api/translations/{project}/{language}` only. Anonymous
  by default (`Auth:PublicBundleReads=true`). ETag-aware, cache-fronted.
- **Management API** — projects, languages, keys, strings, review, review-bulk,
  publish + preview, import, grid, categories, dashboard, missing, history. Each
  route carries a named authorization policy.

Errors become RFC 7807 `application/problem+json` through
`ApplicationExceptionHandler`. Full reference: [`api.md`](api.md).

## Existing Redis architecture

Redis is a **cache only** (`ConnectionStrings:Redis`, StackExchange.Redis
format). When it is unset, an in-process `IDistributedCache` is used and the
service behaves identically. Key `translations:{project}:{language}` (lower-cased)
holds the serialized assembled map plus its content hash; TTL
`Cache:TranslationsTtlMinutes` (default 60). Every cache call is wrapped: a
failure is logged and treated as a miss, so delivery degrades to on-demand
assembly. There is **no Redis readiness probe** — a Redis outage does not make
the service unready. Redis additionally backs the ASP.NET Data Protection key
ring (`DataProtectionSetup`). Details: [`caching.md`](caching.md).

## Existing authentication

Microsoft **Entra ID / OpenID Connect**, `Microsoft.Identity.Web` on both the API
and the Admin UI.

- API validates JWT bearer tokens (`AddMicrosoftIdentityWebApi`, config section
  `AzureAd`). Roles come from the token `roles` claim.
- `Auth:Enabled=false` (set in `appsettings.Development.json`, the compose dev
  stack, and the test factory) swaps in `DevBypassAuthHandler` — every request is
  a synthetic principal holding all roles. **Refused at startup under
  `ASPNETCORE_ENVIRONMENT=Production`.**
- `Auth:PublicBundleReads` (default `true`) keeps the consumer delivery read
  anonymous; `false` makes it require `CanRead`.
- `updatedBy` / `reviewedBy` / `createdBy` are taken from the validated token
  when one is present; the request-body field only applies anonymously or under
  the dev bypass (`TokenActor`).

Details: [`authentication.md`](authentication.md),
[`authorisation.md`](authorisation.md).

## Existing pipelines

- **`.github/workflows/ci.yml`** — the required PR gate. restore → build
  (warnings-as-errors) → `dotnet test` at solution scope with coverage; a
  non-blocking `dotnet format` job. No MongoDB service container (the suites
  self-provision).
- **`azure-pipelines.yml`** — CI + PR build (`Build` stage via
  `.azuredevops/templates/*`), `Package` stage on `main` (`dotnet publish` +
  `docker buildAndPush` to ACR), and a `Deploy` stage. Variable groups
  `ctms-ci` / `ctms-acr`; no secrets in YAML. See
  [`azure-devops.md`](azure-devops.md).

## Existing tests

Three xUnit projects, ~**287** test cases (≈30 client + 188 application + 69
integration — count from `[Fact]`/`[Theory]` methods and `[InlineData]` rows):

- **`CTMS.Application.Tests`** — application services end-to-end against a real
  MongoDB started in-process by `EphemeralMongo` (shared `MongoFixture`,
  `CtmsTestHarness` per class, in-memory `IDistributedCache` for the delivery
  cache). Covers resolution / common-merge / fallback / omit rule / content hash,
  the review state machine (`ReviewWorkflowTests`), import parsers + service,
  bulk review, grid `status` filter + `source` tag, publish preview, the
  authorization policy runtime (`AuthorizationPoliciesTests`), `TokenActor`, and
  `AddTranslationServices` in-process registration
  (`TranslationServiceRegistrationTests`).
- **`CTMS.Api.IntegrationTests`** — the full HTTP surface through
  `WebApplicationFactory` over the real `Program`. `MongoFixture` prefers a real
  `mongo:7` via Testcontainers when a Docker daemon is reachable, else
  `EphemeralMongo`. A test auth handler drives the real `AuthorizationPolicies`
  from header roles. Covers the authorization matrix, actor-from-token, the
  delivery content-hash ETag / `304`, management screens + bulk publish, history
  with value diffs, lifecycle, validation / not-found, health, CORS, rate
  limiting, request-size limits, production startup guard.
- **`CTMS.Client.Tests`** — `CTMS.Client` against a stub `HttpMessageHandler`:
  revalidation / `304` / offline-stale state machine, fallback chain, on-disk
  cache round-trip / atomic write / corruption handling.

`NuGetAudit` is off on the test projects (transitive packages from
EphemeralMongo / Testcontainers); shipping projects keep auditing on, so
`dotnet build` still fails on advisories in product code. Details:
[`architecture.md` §Testing].

## Existing dependencies

Not managed centrally (`ManagePackageVersionsCentrally=false`). Notable runtime
packages: `MongoDB.Driver`, `StackExchange.Redis` +
`Microsoft.Extensions.Caching.StackExchangeRedis`, `Microsoft.Identity.Web`,
`Microsoft.AspNetCore.Authentication.JwtBearer`, `Swashbuckle.AspNetCore`,
`Microsoft.AspNetCore.DataProtection.StackExchangeRedis`. Test-only:
`EphemeralMongo`, `Testcontainers.MongoDb`, `xunit`,
`coverlet.collector`, `Microsoft.AspNetCore.Mvc.Testing`.

## Unused-code candidates / obsolete data / removed features

Commit `c34515a` already removed the features the earlier spec had added but the
current spec excludes. **Recorded here so the removal is not re-litigated:**

| Removed | What it was | Why it is gone |
|---|---|---|
| **API-key auth** (`X-Api-Key`, `ApiKey` aggregate, `apiKeys` collection, `POST/GET/DELETE /api/api-keys`) | A read-only credential for machine callers, composed with the JWT scheme. | The current spec has exactly two consumption paths — in-process `ITranslationService` and the anonymous-by-default REST read. A second credential type is surface the spec does not call for. |
| **Publish webhooks** (`Webhook` aggregate, `webhooks` collection, `/api/webhooks`, `X-CTMS-Signature`, the dispatch `BackgroundService`) | Push notification of a publish to registered URLs. | The spec's change-detection contract is ETag + `If-None-Match` + `304`. Webhooks were best-effort extra surface; consumers still had to fall back to conditional GET. |
| **CSV and RESX import parsers** | Two of four `TranslationFileParser` formats. | The spec's import path is "JSON + flat". `TranslationFileParser.SupportedFormats` is now `["json", "flat"]`. Migration from `.resx` / spreadsheets is a one-off script or a pre-conversion to JSON — see [`migration.md`](migration.md). |
| **Static language catalogue** (`LanguageCatalogue`, `GET /api/languages/suggestions`, `LanguageSuggestionDto`) | A ~38-entry hard-coded BCP-47 picklist for the new-project wizard. | A hard-coded list is a code-change-per-locale. `POST /api/languages` / `POST /api/languages/bulk` accept any BCP-47 code; the Admin UI can ship its own picklist. |
| **Versioned `TranslationBundle`** (aggregate, `translationBundles` collection, `/bundles`, `/versions`) | Immutable per-`(project, locale)` snapshots with a monotonic `Version`. | Superseded by assemble-on-demand delivery (see below). Removed before `c34515a` (ADR 0004). |
| **`TranslationString.Version` optimistic-concurrency token** | `expectedVersion` in, `version` out, `409` + `currentVersion`. | Spec §27: no numeric versioning. String upsert is last-write-wins; mitigations are the review workflow and the audit trail. |
| **EF Core + PostgreSQL** (`CtmsDbContext`, `IEntityTypeConfiguration<T>`, `InitialCreate`, `.config/dotnet-tools.json` / `dotnet-ef`) | The original relational scaffold. | Replaced by MongoDB (ADR 0002). |
| **Per-project `Locale` aggregate** | Each project re-declared its languages. | Replaced by the global `Language` catalogue with per-project `EnabledLanguageCodes` (ADR 0004). |

### Recommended changes / removals already applied

- Vocabulary is aligned to the spec: **application → project** on the wire
  (`/api/projects/*`, `ProjectDto.Code`, route parameter `project`);
  **shared → common** (`Project.IsCommon`, seeded `common` project);
  review states `Draft / InReview / Approved / Published / Archived`; roles
  `TranslationAdministrator / TranslationManager / TranslationReviewer /
  Translator / TranslationReadOnly`.
- `ITranslationService` is the single public in-process abstraction and is what
  `TranslationServiceRegistrationTests` verifies can be registered and called
  without HTTP.
- Health endpoints are `/health`, `/health/live`, `/health/ready` (spec §48).

### Still-present, kept deliberately

- **`CTMS.Client`** and **`samples/`** — spec §38–§39 explicitly permit an
  optional client library. It is a client of the API and does not replace the
  service.
- **`CTMS.AdminUI`** — spec §33.
- The `Detail` field on `AuditEntry` and the `AuditAction` values `Archived` /
  `Unarchived` — used by the `archive` / `unarchive` review actions.

## Migration risks

- **Last-write-wins string upsert.** Two editors saving the same
  `(key, language)` — the later write silently wins. Mitigations: the Admin UI is
  the only interactive writer; editing any non-`Draft` string drops it to
  `InReview`; the audit trail records `OldValue` / `NewValue` on every edit.
- **Referential integrity is the application's job.** A
  `TranslationString.LanguageCode` is a bare string; nothing at the DB level ties
  it to a `Language` row or to a project's enabled set. Services validate on
  write; a direct DB write bypasses that.
- **A `common` (shared) publish fans invalidation out to every project** ×
  affected languages — a burst of cache invalidations and a wave of
  re-assembly on the next request per pair.
- **On-demand assembly does more work per cache miss** than serving a stored
  blob (several collection reads, per-key fallback walk, content hash).
  Mitigated by the read-through cache and the `304` path.
- **No multi-document transactions.** A publish updates many strings and writes
  many audit entries across collections non-transactionally; it relies on
  operation ordering and idempotency. A mid-run failure leaves partial work
  (re-runnable).
- **Importing existing data** (`.resx`, spreadsheets, CSV) needs a pre-conversion
  to JSON or flat, or a one-off script hitting the string upsert. See
  [`migration.md`](migration.md). Do not auto-delete the source data.
- **Cosmos DB for MongoDB (RU)** semantics differ from a real `mongod`
  (`retrywrites=false` required, some aggregation limits). The deploy Bicep
  defaults to the RU serverless account.
