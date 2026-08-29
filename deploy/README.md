# CTMS deployment

## Local stack (Docker Compose)

From the repository root:

```bash
cp .env.example .env          # optional — sensible defaults are baked in
docker compose up --build
```

`docker-compose.yml` is the **development** posture and only that:
`ASPNETCORE_ENVIRONMENT=Development`, `Seed__Enabled=true`, `Auth__Enabled=false`,
`RateLimit__Enabled=false`.

### Production-style run

Layer `docker-compose.prod.yml` on top to flip the posture (auth on, seed off,
rate limiting on, hardened read-only container, mongo/redis no longer published
to the host):

```bash
# provide the required vars first (see .env.example): CTMS_ALLOWED_ORIGIN,
# AZUREAD_TENANT_ID, AZUREAD_CLIENT_ID, AZUREAD_AUDIENCE
docker compose -f docker-compose.yml -f docker-compose.prod.yml config    # verify
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

Services:

| Service | Image | Host port | Purpose |
|---|---|---|---|
| `api`   | built from `./Dockerfile` | `8080` | CTMS ASP.NET Core API. Swagger at http://localhost:8080/swagger (Development). Liveness `GET /health`, readiness `GET /health/ready`. |
| `mongo` | `mongo:7` | `27017` | Primary datastore. Named volume `mongo-data` persists between runs. Healthcheck: `mongosh --eval "db.adminCommand('ping')"`. |
| `redis` | `redis:7-alpine` | `6379` | Cache for the translation-bundle endpoint. Persistence disabled (cache only). Healthcheck: `redis-cli ping`. |

`api` waits for `mongo` and `redis` to report healthy before it starts.

### Hot-reload variant (optional)

```bash
cp docker-compose.override.yml.example docker-compose.override.yml
docker compose up
```

`docker-compose.override.yml` is git-ignored. It bind-mounts `./src` and runs
`dotnet watch`, so edits rebuild in-container. Delete the file to go back to the
published-image behaviour.

## Configuration keys

The API reads these (double-underscore = config section separator):

| Env var | Config key | Local value | Meaning |
|---|---|---|---|
| `ConnectionStrings__CtmsDatabase` | `ConnectionStrings:CtmsDatabase` | `mongodb://mongo:27017` | MongoDB connection string. |
| `Mongo__Database` | `Mongo:Database` | `ctms` | Mongo database name. |
| `ConnectionStrings__Redis` | `ConnectionStrings:Redis` | `redis:6379` | Redis (StackExchange.Redis format: `host:port[,options]`). Backs the bundle cache **and** the Data Protection key ring — keys persist to Redis and survive an `api` restart. |
| `ASPNETCORE_ENVIRONMENT` | — | `Development` | Enables Swagger. `docker-compose.prod.yml` → `Production`. |
| `Seed__Enabled` | `Seed:Enabled` | `true` | Seed demo data on startup. `docker-compose.prod.yml` → `false`. |
| `Auth__Enabled` | `Auth:Enabled` | `false` | Dev bypass (synthetic all-roles principal). `docker-compose.prod.yml` → `true` (+ `AzureAd__*`). |
| `Auth__PublicBundleReads` | `Auth:PublicBundleReads` | `true` | Allow anonymous reads of the published bundle even when auth is on. |
| `AzureAd__Instance` / `AzureAd__TenantId` / `AzureAd__ClientId` / `AzureAd__Audience` | `AzureAd:*` | unset | Entra ID app registration the API validates bearer tokens against. Required by the prod override (from `${AZUREAD_*}`). The API is a token **validator** — no client secret. |
| `RateLimit__Enabled` | `RateLimit:Enabled` | `false` | Global fixed-window rate limiter. Off locally so manual testing doesn't 429. `docker-compose.prod.yml` → `true`. |
| `RateLimit__PermitPerWindow` / `WindowSeconds` / `QueueLimit` / `BundlePermitPerWindow` | `RateLimit:*` | app defaults (120 / 60 / 0 / permit×5) | Rate-limiter tuning. Passed through by the prod override from `${RATELIMIT_*}`. |
| `Cors__AllowedOrigins__0` | `Cors:AllowedOrigins[0]` | unset | CORS allow-list. Empty/absent → the API allows no cross-origin request. Prod override sets it from `${CTMS_ALLOWED_ORIGIN}`. Add `__1`, `__2` … for more origins. |
| `Limits__MaxRequestBodyBytes` | `Limits:MaxRequestBodyBytes` | unset → 262144 (256 KB) | Max request body; over-limit → `413`. Left at the app default in both compose files. |

## Pointing at a real Cosmos DB / Redis

Compose reads `.env`; override the connection values there (or export them):

```dotenv
# Cosmos DB for MongoDB (RU or vCore) — full connection string from the portal / az cli
CTMS_DATABASE__CONNECTION_STRING=mongodb+srv://<user>:<password>@<account>.mongocluster.cosmos.azure.com/?tls=true&authMechanism=SCRAM-SHA-256&retrywrites=false
MONGO_DATABASE=ctms

# Azure Cache for Redis
CTMS_REDIS__CONNECTION_STRING=<name>.redis.cache.windows.net:6380,password=<key>,ssl=True,abortConnect=False

ASPNETCORE_ENVIRONMENT=Staging
SEED_ENABLED=false
```

Then `docker compose up api` (you can stop the local `mongo` / `redis`
containers — nothing else depends on them). For a full Azure deployment rather
than a local container pointed at Azure, see [`azure/README.md`](azure/README.md).

## Image

`./Dockerfile` (context = repo root) is a multi-stage build:
`mcr.microsoft.com/dotnet/sdk:10.0` restores the **API project graph only**
(`src/CTMS.Api/CTMS.Api.csproj` → Application → Domain, Infrastructure — the
`.csproj` layer is copied first for caching) and publishes `src/CTMS.Api`;
`mcr.microsoft.com/dotnet/aspnet:10.0` runs it as the non-root `app` user on port
**8080**, HTTP only (`ASPNETCORE_URLS=http://+:8080`, no HTTPS binding inside the
container — TLS is terminated upstream).

- **`HEALTHCHECK`** is baked in — it hits `http://localhost:8080/health` over a
  raw TCP socket with `bash` (the aspnet base image has no `curl`/`wget`). A
  compose healthcheck still overrides it.
- **Read-only-rootfs friendly.** `TMPDIR` and `DOTNET_BUNDLE_EXTRACT_BASE_DIR`
  point at `/tmp`, so `docker-compose.prod.yml` can run the container with
  `read_only: true` + `cap_drop: [ALL]` + `no-new-privileges` and only a
  `tmpfs: /tmp`. Verified: the API boots and serves `/health` in that mode.
- **`.dockerignore`** keeps the build context lean — `tests/`, `samples/`,
  `docs/`, `deploy/`, `.github/`, `.azuredevops/`, `src/CTMS.AdminUI/` and
  `src/CTMS.Client*/` are all excluded (the image only needs the API graph).
