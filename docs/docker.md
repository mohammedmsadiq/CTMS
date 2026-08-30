# Docker

Local development runs the API + MongoDB + Redis with Docker Compose. Two compose
files: `docker-compose.yml` is the dev posture and nothing else;
`docker-compose.prod.yml` layers the production posture on top. **No production
secrets live in either file** — only `${VAR}` interpolation.

---

## The image — root `Dockerfile`

Multi-stage; **build context = repo root** (`docker build -t ctms-api .`).

- **build stage** (`mcr.microsoft.com/dotnet/sdk:10.0`) — copies `global.json`,
  `Directory.Build.props`, and just the **API project graph** `.csproj` files
  (`CTMS.Api` → `CTMS.Application` → `CTMS.Domain`, `CTMS.Infrastructure`),
  restores, then copies the rest and `dotnet publish`es `src/CTMS.Api`. The
  AdminUI, client library, samples, and tests are **not** in the runtime image.
- **runtime stage** (`mcr.microsoft.com/dotnet/aspnet:10.0`) — HTTP only on
  **8080** (`ASPNETCORE_URLS=http://+:8080`, `ASPNETCORE_HTTPS_PORTS=` cleared —
  TLS terminates upstream). Runs as the non-root `app` user (uid 1654).
- **`HEALTHCHECK`** baked in — hits `http://localhost:8080/health` over a raw TCP
  socket with `bash` (the aspnet base image has no `curl`/`wget`). A compose
  healthcheck can still override it.
- **Read-only-rootfs friendly** — `TMPDIR` and
  `DOTNET_BUNDLE_EXTRACT_BASE_DIR` point at `/tmp`, so the prod override can run
  the container `read_only: true` + `cap_drop: [ALL]` + `no-new-privileges` with
  only a `tmpfs: /tmp`.
- `.dockerignore` keeps the context lean — `tests/`, `samples/`, `docs/`,
  `deploy/`, `.github/`, `.azuredevops/`, `src/CTMS.AdminUI/`, `src/CTMS.Client*/`
  are excluded.

## `docker-compose.yml` — development stack

```bash
docker compose up --build
```

| Service | Image | Host port | Role |
|---|---|---|---|
| `mongo` | `mongo:7` | `27017` | Primary datastore. Volume `mongo-data`. Healthcheck: `mongosh … ping`. |
| `redis` | `redis:7-alpine` | `6379` | Delivery cache **and** the Data Protection key ring. Persistence off (`--save "" --appendonly no`). Healthcheck: `redis-cli ping`. |
| `api` | built from `./Dockerfile` | `8080` | The API. `depends_on` both datastores' healthchecks. Swagger at `/swagger`. |

Environment the `api` service sets (all overridable via `.env` — see
`.env.example`):

| Env var | Default | Config key |
|---|---|---|
| `ConnectionStrings__CtmsDatabase` | `mongodb://mongo:27017` | `ConnectionStrings:CtmsDatabase` |
| `Mongo__Database` | `ctms` | `Mongo:Database` |
| `ConnectionStrings__Redis` | `redis:6379` | `ConnectionStrings:Redis` |
| `ASPNETCORE_ENVIRONMENT` | `Development` | — |
| `Seed__Enabled` | `true` | `Seed:Enabled` |
| `Auth__Enabled` | `false` | `Auth:Enabled` — synthetic all-roles principal, no Entra ID needed |
| `Auth__PublicBundleReads` | `true` | `Auth:PublicBundleReads` |
| `RateLimit__Enabled` | `false` | so hammering the API by hand does not `429` |

### Hot-reload variant

```bash
cp docker-compose.override.yml.example docker-compose.override.yml   # git-ignored
docker compose up
```

Bind-mounts `./src` and runs `dotnet watch` inside the container (SDK stage).
Delete the file to go back to the published image.

## `docker-compose.prod.yml` — production posture

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml config    # verify
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d
```

It flips the dev posture and hardens the runtime:

| | dev | prod override |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Development` | `Production` |
| `Seed__Enabled` | `true` | `false` |
| `Auth__Enabled` | `false` | `true` (+ `AzureAd__*` from the env) |
| `RateLimit__Enabled` | `false` | `true` (+ `RateLimit__*` tuning) |
| `Cors__AllowedOrigins__0` | unset | `${CTMS_ALLOWED_ORIGIN}` |
| mongo / redis host ports | published | `!reset []` — internal only |
| redis persistence | off | `--appendonly yes` (the key ring must survive a restart) |
| api container | — | `read_only: true`, `cap_drop: [ALL]`, `no-new-privileges`, `tmpfs: /tmp`, CPU/memory limits |

Required env for the prod run (all valueless placeholders in `.env.example`):
`CTMS_ALLOWED_ORIGIN`, `AZUREAD_TENANT_ID`, `AZUREAD_CLIENT_ID`,
`AZUREAD_AUDIENCE`. **The API is a bearer-token validator — it needs no client
secret.** Provide real values via a deployment `.env`, `--env-file`, or the shell
environment.

## Pointing at a real Cosmos DB / Redis

Override the connection values in `.env`:

```dotenv
CTMS_DATABASE__CONNECTION_STRING=mongodb+srv://<user>:<pw>@<acct>.mongocluster.cosmos.azure.com/?tls=true&authMechanism=SCRAM-SHA-256&retrywrites=false
MONGO_DATABASE=ctms
CTMS_REDIS__CONNECTION_STRING=<name>.redis.cache.windows.net:6380,password=<key>,ssl=True,abortConnect=False
ASPNETCORE_ENVIRONMENT=Staging
SEED_ENABLED=false
```

Then `docker compose up api` and stop the local `mongo` / `redis` containers. For
a full Azure deployment (not a local container pointed at Azure), see
[`azure-deployment.md`](azure-deployment.md).

## No secrets in compose

Both compose files use only `${VAR}` interpolation; every variable is listed
(commented, valueless) in `.env.example`. `.env` is git-ignored. `appsettings.json`
ships a passwordless localhost Mongo placeholder. Spec §47, §53.
