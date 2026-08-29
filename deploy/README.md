# CTMS deployment

## Local stack (Docker Compose)

From the repository root:

```bash
cp .env.example .env          # optional — sensible defaults are baked in
docker compose up --build
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
| `ConnectionStrings__Redis` | `ConnectionStrings:Redis` | `redis:6379` | Redis (StackExchange.Redis format: `host:port[,options]`). |
| `ASPNETCORE_ENVIRONMENT` | — | `Development` | Enables Swagger. |
| `Seed__Enabled` | `Seed:Enabled` | `true` | Seed demo data on startup. |

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
`mcr.microsoft.com/dotnet/sdk:10.0` restores (`.sln` + `.csproj` layer first for
caching) and publishes `src/CTMS.Api`; `mcr.microsoft.com/dotnet/aspnet:10.0`
runs it as the non-root `app` user on port **8080**, HTTP only
(`ASPNETCORE_URLS=http://+:8080`, no HTTPS binding inside the container — TLS is
terminated upstream).
