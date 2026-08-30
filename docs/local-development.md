# Local development

Build, run, and test CTMS on your machine. The commands are the ones in
[`CLAUDE.md`](../CLAUDE.md) — that file is the source of truth; this page adds
the surrounding setup.

---

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10.0.x (pinned `10.0.400`, `rollForward: latestFeature` in `global.json`) | `dotnet --info` should list a 10.0 SDK |
| Docker | Docker Desktop / Engine + Compose v2 | To run MongoDB + Redis for the app and to build the API image. **Not** needed for `dotnet test` — the suites embed their own MongoDB via `EphemeralMongo`. |
| Git | any recent | |

No IDE required — the solution builds and tests from the CLI. There are **no
local dotnet tools**; skip `dotnet tool restore` (there is no `dotnet ef`).

## 1. Clone and restore

```bash
git clone <repo-url> ctms
cd ctms
dotnet restore CTMS.sln
```

## 2. Start MongoDB + Redis

The API needs MongoDB; the delivery cache uses Redis when
`ConnectionStrings:Redis` is set (otherwise an in-process cache). Bring both up
with Docker Compose from the repo root:

```bash
cp .env.example .env       # optional — sensible defaults are baked in
docker compose up -d       # mongo + redis (+ api if you want the container too)
docker compose ps          # check health
docker compose down        # stop; add -v to wipe the mongo-data volume
```

`docker-compose.yml` is the **development** posture only:
`ASPNETCORE_ENVIRONMENT=Development`, `Seed__Enabled=true`, `Auth__Enabled=false`,
`RateLimit__Enabled=false`. See [`docker.md`](docker.md).

| Service | Image | Host port | Purpose |
|---|---|---|---|
| `mongo` | `mongo:7` | `27017` | Primary datastore. Named volume `mongo-data` persists between runs. |
| `redis` | `redis:7-alpine` | `6379` | Delivery cache + Data Protection key ring. Persistence disabled in dev. |
| `api` | built from `./Dockerfile` | `8080` | The API. Waits for `mongo` + `redis` healthchecks. |

Local-only tweaks go in `docker-compose.override.yml` (git-ignored); see
`docker-compose.override.yml.example` for a `dotnet watch` hot-reload variant.

## 3. Run the API

```bash
dotnet run --project src/CTMS.Api
```

- Listens on `http://localhost:5147` (and `https://localhost:7219` with the
  `https` launch profile). In the container it is `http://localhost:8080`.
- Swagger UI: <http://localhost:5147/swagger> (Development only).
- Health: `/health`, `/health/live` (liveness), `/health/ready` (Mongo `ping`).

With `docker compose up mongo -d` running, the default `appsettings.json`
(`mongodb://localhost:27017`, database `ctms`) works with no extra config.
`dotnet run` **always needs a reachable MongoDB**.

Point the host-run API at compose's Mongo/Redis:

```bash
export ConnectionStrings__CtmsDatabase="mongodb://localhost:27017"
export Mongo__Database="ctms"
export ConnectionStrings__Redis="localhost:6379"
dotnet run --project src/CTMS.Api
```

## 4. Run the Admin UI

```bash
dotnet run --project src/CTMS.AdminUI
```

- Blazor Web App (InteractiveServer). `Auth:Enabled=false` locally → every
  visitor is a synthetic all-roles principal and the API is called with no token.
- Points at the API via `Ctms:ApiBaseUrl` (default `http://localhost:8080`; set
  it to `http://localhost:5147` when you run the API with `dotnet run`).
- With real auth (`Auth:Enabled=true`) you also need `Ctms:ApiScope` and the
  `AzureAd:*` values. See [`authentication.md`](authentication.md).

## 5. Run the tests

```bash
dotnet test                     # whole suite (~287 cases)
```

Run one test:

```bash
dotnet test --filter "FullyQualifiedName~PublishedTranslationsServiceTests"
# or
dotnet test --filter "DisplayName~fallback chain"
```

- `tests/CTMS.Application.Tests` drives the application services against a real
  MongoDB started in-process by **`EphemeralMongo`** (a shared `MongoFixture`;
  each class wraps it in a `CtmsTestHarness` with every production index applied
  and an in-memory `IDistributedCache` for the delivery cache).
  `ReviewWorkflowTests` exercises the `TranslationString` transitions directly.
  `TranslationServiceRegistrationTests` proves `AddTranslationServices` +
  `ITranslationService` works with no HTTP.
- `tests/CTMS.Api.IntegrationTests` runs the real `Program` through
  `WebApplicationFactory`. `MongoFixture` prefers a real `mongo:7` via
  `Testcontainers.MongoDb` when a Docker daemon is reachable, else
  `EphemeralMongo`.
- `tests/CTMS.Client.Tests` runs `CTMS.Client` against a stub `HttpMessageHandler`.
- **No Docker needed for `dotnet test`** — EphemeralMongo downloads / embeds the
  `mongod` binary on first use (cached at `~/.cache/ephemeral-mongo`).

The build is **warnings-as-errors** (`Directory.Build.props`), so `dotnet build`
and `dotnet test` fail on any analyzer/compiler warning. `NuGetAudit` is off on
the test projects only; shipping projects still fail the build on advisories.

## 6. The seeder toggle

`DataSeeder` seeds a demo dataset on startup, **only** when both:

1. `ASPNETCORE_ENVIRONMENT=Development`, and
2. `Seed:Enabled` is `true`.

`appsettings.Development.json` ships `Seed:Enabled: false`; turn it on for a run:

```bash
Seed__Enabled=true dotnet run --project src/CTMS.Api
```

(`docker compose` already sets `Seed__Enabled=true` on the `api` service.) It is
idempotent — nothing happens if the `common` project already exists. What it
seeds: [`database.md` → Seeder](database.md#seeder).

## 7. Build the container image

The root `Dockerfile` is a multi-stage build; **the build context must be the
repo root**:

```bash
docker build -t ctms-api .
docker run --rm -p 8080:8080 \
  -e ConnectionStrings__CtmsDatabase="mongodb://host.docker.internal:27017" \
  -e Mongo__Database="ctms" \
  ctms-api
```

The runtime image listens HTTP-only on `:8080` (TLS terminated upstream) and
runs as the non-root `app` user. See [`docker.md`](docker.md).

## 8. Contributing

- Branch off `main`; open a PR. Two CI systems run restore → build
  (warnings-as-errors) → tests with coverage on every PR to `main`:
  [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) (GitHub Actions — the
  required check) and [`azure-pipelines.yml`](../azure-pipelines.yml) (Azure
  DevOps — also packages + pushes the API image on `main`). See
  [`azure-devops.md`](azure-devops.md).
- **Update docs in the same change as the behaviour** — `CLAUDE.md` for
  commands/architecture, `docs/api.md` for endpoint changes, `docs/adr/` for
  hard-to-reverse decisions.
