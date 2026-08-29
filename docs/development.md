# Local development

How to build, run and test CTMS on your machine. Commands are the ones from
[`CLAUDE.md`](../CLAUDE.md) - that file is the source of truth; this page adds
the surrounding setup.

> **Backend mid-rewrite.** Persistence has moved from PostgreSQL / EF Core to
> MongoDB ([ADR&nbsp;0002](adr/0002-mongodb-as-primary-store.md)). `CLAUDE.md`
> still documents the EF/`dotnet-ef` workflow in places; where this page and
> `CLAUDE.md` disagree on persistence, this page reflects the current tree.
> Some test classes had not been ported to the Mongo harness at the time of
> writing - `dotnet test` may not be green until that lands.

---

## Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| .NET SDK | 10.0.x (pinned to `10.0.400`, `rollForward: latestFeature` in `global.json`) | `dotnet --info` should list a 10.0 SDK. |
| Docker | Docker Desktop or Engine + Compose v2 | To run Mongo + Redis for the app, and to build the API image. **Not** required for `dotnet test` - the test suite embeds its own MongoDB via `EphemeralMongo`. |
| Git | any recent | - |

No IDE required; the solution builds and tests from the CLI. Swagger UI
(`/swagger`) is available in the `Development` environment.

There are **no local dotnet tools** - `.config/dotnet-tools.json` (which pinned
`dotnet-ef`) was removed with the MongoDB switch. Skip `dotnet tool restore`.

---

## 1. Clone and restore

```bash
git clone <repo-url> ctms
cd ctms
dotnet restore CTMS.sln
```

---

## 2. Start MongoDB + Redis

The API needs MongoDB; the (planned) bundle cache needs Redis. Bring both up
with Docker Compose from the repo root:

```bash
cp .env.example .env           # optional - sensible defaults are baked in
docker compose up -d           # mongo + redis (+ api, if you want the container)
docker compose ps              # check health
docker compose down            # stop; add -v to wipe the mongo-data volume
```

Compose services (`docker-compose.yml`, owned by the infra/devops workstream):

| Service | Image | Host port | Purpose |
|---------|-------|-----------|---------|
| `mongo` | `mongo:7` | `27017` | Primary datastore. Named volume `mongo-data` persists between runs. |
| `redis` | `redis:7-alpine` | `6379` | Cache for the (planned) bundle endpoint. Persistence disabled. |
| `api` | built from `./Dockerfile` | `8080` | The API. Waits for `mongo` + `redis` healthchecks. Sets `Seed__Enabled=true`. |

Local-only tweaks go in `docker-compose.override.yml` (git-ignored); see
`docker-compose.override.yml.example` for a `dotnet watch` hot-reload variant.
More detail, including pointing at Azure Cosmos DB for MongoDB / Azure Cache for
Redis, is in [`deploy/README.md`](../deploy/README.md).

Config the app expects (override via environment; `__` maps to `:`):

| Config key | Env override | Compose default |
|------------|--------------|-----------------|
| `ConnectionStrings:CtmsDatabase` | `ConnectionStrings__CtmsDatabase` | `mongodb://mongo:27017` (`mongodb://localhost:27017` when running the API on the host) |
| `Mongo:Database` | `Mongo__Database` | `ctms` |
| `ConnectionStrings:Redis` | `ConnectionStrings__Redis` | `redis:6379` |
| `Seed:Enabled` | `Seed__Enabled` | `true` in compose; `false` in `appsettings.Development.json` |

---

## 3. Run the API

```bash
dotnet run --project src/CTMS.Api
```

- Listens on `http://localhost:5147` (and `https://localhost:7219` with the
  `https` launch profile). In the container it is `http://localhost:8080`.
- Swagger UI: <http://localhost:5147/swagger> (Development only).
- Health: `/health` (liveness) and `/health/ready` (Mongo `ping`).

With `docker compose up mongo -d` running, the default
`appsettings.json` (`mongodb://localhost:27017`, database `ctms`) works with no
extra configuration.

### How indexes are created

`MongoIndexInitializer` - an `IHostedService` registered by `AddInfrastructure` -
runs on every startup and calls `createIndexes` for all six collections
(unique on `projects.slug`, `locales.(projectId,code)`,
`translationKeys.(projectId,keyName)`,
`translationStrings.(translationKeyId,localeId)`,
`translationBundles.(projectId,localeCode,version)`; non-unique lookup indexes on
`auditEntries`). `createIndexes` is idempotent, so a fresh database is ready
after the first boot - there is no migration step and no `dotnet ef`.

### Dev data seeder

`DataSeeder` (also an `IHostedService`) seeds one sample project on startup,
**only** when both are true:

1. `ASPNETCORE_ENVIRONMENT=Development`, and
2. `Seed:Enabled` is `true`.

`appsettings.Development.json` ships `Seed:Enabled: false`; turn it on for a run
with:

```bash
Seed__Enabled=true dotnet run --project src/CTMS.Api
```

(`docker compose` already sets `Seed__Enabled=true` on the `api` service.) It is
idempotent - it does nothing if a project with slug `marketing-site` already
exists - and seeds that project, `en` / `fr` / `ar` locales, and five English
strings across `Draft` / `NeedsReview` / `Approved` states.

---

## 4. Run the tests

```bash
dotnet test                       # whole suite
```

Run a single test:

```bash
dotnet test --filter "FullyQualifiedName~ProjectServiceTests.CreateAsync_rejects_a_duplicate_slug"
# or
dotnet test --filter "DisplayName~duplicate slug"
```

- `tests/CTMS.Application.Tests` (xUnit) drives the application services against
  real repositories on a real MongoDB provided by **`EphemeralMongo`** - a
  `MongoFixture` starts a throwaway `mongod`, shared through the `"mongo"` xUnit
  collection; each class wraps it in a `CtmsTestHarness`. `ReviewWorkflowTests`
  exercises the `TranslationString` transitions directly against the domain type.
- **No Docker needed for `dotnet test`.** EphemeralMongo downloads / embeds the
  `mongod` binary itself. (CI additionally attaches a `mongo:7` service
  container so the suite can be repointed at an external server with one env-var
  change.)
- The test project sets `<NuGetAudit>false</NuGetAudit>` because EphemeralMongo
  pulls older `SharpCompress` / `Snappier` transitively. The shipping projects
  keep auditing on, so `dotnet build` still fails on advisories in product code.

The build is warnings-as-errors (`Directory.Build.props`), so `dotnet build` and
`dotnet test` fail on any analyzer/compiler warning. Keep the build clean.

---

## 5. Configuration and secrets

Settings resolve in this order (later wins): `appsettings.json` ->
`appsettings.{Environment}.json` -> environment variables (`__` maps to `:`).

- **Never commit credentials.** `appsettings.json` ships a passwordless
  localhost placeholder (`mongodb://localhost:27017`).
  `appsettings.Development.json` and `.env` are git-ignored; `.env.example` is
  the committed template. Real environments inject
  `ConnectionStrings__CtmsDatabase` / `ConnectionStrings__Redis` as secrets
  (pipeline variable groups; Key Vault references in Azure - see
  [`deploy/azure/README.md`](../deploy/azure/README.md)).
- Example, API on the host against compose's Mongo/Redis:

  ```bash
  export ConnectionStrings__CtmsDatabase="mongodb://localhost:27017"
  export Mongo__Database="ctms"
  export ConnectionStrings__Redis="localhost:6379"
  dotnet run --project src/CTMS.Api
  ```

---

## 6. Build the container image

The root `Dockerfile` is a multi-stage build; **the build context must be the
repo root**:

```bash
docker build -t ctms-api .
docker run --rm -p 8080:8080 \
  -e ConnectionStrings__CtmsDatabase="mongodb://host.docker.internal:27017" \
  -e Mongo__Database="ctms" \
  ctms-api
```

The runtime image listens HTTP-only on `:8080` (TLS terminated by the
ingress/platform) and runs as the non-root `app` user.

---

## 7. Contributing

- Branch off `main`; open a PR. CI (`azure-pipelines.yml`) runs restore, build
  (warnings-as-errors), and tests with coverage on every PR to `main`.
- Update docs in the same change as the behaviour: `CLAUDE.md` for
  commands/architecture, `docs/api.md` for endpoint changes, `docs/adr/` for
  hard-to-reverse decisions.
