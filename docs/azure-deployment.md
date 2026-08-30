# Azure deployment

`deploy/azure/main.bicep` is a **first-draft scaffold** for hosting CTMS on Azure
Container Apps. It compiles (`az bicep build`) but has not been deployed; every
`// TODO` is a real gap (networking, diagnostics, backup, throughput sizing).

This page describes what the Bicep provisions and how the container app is
wired. The CI/CD wiring that calls it is in [`azure-devops.md`](azure-devops.md).
_This doc does not modify `deploy/`._

---

## What `main.bicep` provisions

Scope: resource group. Parameters: `namePrefix` (default `ctms`),
`environmentName` (`dev` / `test` / `prod`), `location`, `apiImage`,
`cosmosMode` (`ru` / `vcore`), `mongoDatabaseName`, `allowedOrigin`,
`azureAd*`, `kvAdminPrincipalId`, `mongoVCore*`.

| Resource | Type | Notes |
|---|---|---|
| Container Registry | `Microsoft.ContainerRegistry/registries` | Basic SKU, admin user **disabled** — pull is via managed identity only |
| User-assigned managed identity | `Microsoft.ManagedIdentity/userAssignedIdentities` | Attached to the API container app; holds the role assignments below |
| Role assignment — **AcrPull** | on the ACR | Lets the app pull images with no registry credentials |
| Role assignment — **Key Vault Secrets User** | on the Key Vault | Lets the app resolve secret references at runtime |
| Role assignment — **Key Vault Secrets Officer** | on the Key Vault | Optional; only when `kvAdminPrincipalId` is supplied, so an operator can seed secret values |
| Key Vault | `Microsoft.KeyVault/vaults` | RBAC authorization, soft-delete on; purge protection on for `prod` |
| Cosmos DB for MongoDB | `databaseAccounts` (RU serverless, default) **or** `mongoClusters` (vCore, a stub) | `cosmosMode` picks. RU path creates the `Mongo:Database` database. |
| Azure Cache for Redis | `Microsoft.Cache/redis` | Basic C0, TLS 1.2, non-SSL port disabled |
| Log Analytics workspace | `Microsoft.OperationalInsights/workspaces` | Backing store for Container Apps logs; 30-day retention |
| Container Apps environment | `Microsoft.App/managedEnvironments` | Wired to Log Analytics |
| API container app | `Microsoft.App/containerApps` | External ingress on **8080**, `allowInsecure: false`, HTTP concurrency scale rule (1–5 replicas), `/health` liveness + `/health/ready` readiness probes. Pulls its image via the managed identity. |

## Configuration contract — container-app env

| Env var | Config key | Source |
|---|---|---|
| `ConnectionStrings__CtmsDatabase` | `ConnectionStrings:CtmsDatabase` | Key Vault secret `CtmsDatabase-ConnectionString` (via `secretRef`) |
| `Mongo__Database` | `Mongo:Database` | `mongoDatabaseName` parameter (plain value) |
| `ConnectionStrings__Redis` | `ConnectionStrings:Redis` | Key Vault secret `Redis-ConnectionString`. Backs **both** the delivery cache **and** the Data Protection key ring. |
| `ASPNETCORE_ENVIRONMENT` | — | `Production` for `prod`, else `Staging` |
| `ASPNETCORE_URLS` | — | `http://+:8080` (TLS terminated at the ingress) |
| `Seed__Enabled` | `Seed:Enabled` | `false` |
| `RateLimit__Enabled` | `RateLimit:Enabled` | `true` |
| `Cors__AllowedOrigins__0` | `Cors:AllowedOrigins[0]` | `allowedOrigin` parameter — the whole entry is **omitted when the param is empty**, and then the API allows no cross-origin request |
| `AzureAd__Instance` / `__TenantId` / `__ClientId` / `__Audience` | `AzureAd:*` | `azureAd*` parameters (plain, non-secret) — the whole block is **omitted when `azureAdTenantId` is empty**, and the image then falls back to its `appsettings` `AzureAd` section |

Auth is **on** in Azure by the image's `appsettings.json` default
(`Auth:Enabled=true`) because the environment is `Staging` / `Production`; the
Bicep never sets `Auth__Enabled`, and `Auth:Enabled=false` would be refused at
startup anyway.

## Secrets expected in Key Vault

The Bicep wires **Key Vault references** — it does not create the values. Create
them after the first deployment (before the container app's next revision
starts):

| Secret name | Value |
|---|---|
| `CtmsDatabase-ConnectionString` | Mongo connection string. RU: `az cosmosdb keys list --type connection-strings`. vCore: the cluster connection string with the admin password substituted in. |
| `Redis-ConnectionString` | `<redisHostName>:6380,password=<primaryKey>,ssl=True,abortConnect=False` |

```bash
az keyvault secret set --vault-name <keyVaultName> \
  --name CtmsDatabase-ConnectionString --value "<mongo-connection-string>"
az keyvault secret set --vault-name <keyVaultName> \
  --name Redis-ConnectionString --value "<host>:6380,password=<key>,ssl=True,abortConnect=False"
```

**The API needs no Entra ID client secret** — it is a bearer-token *validator*.
`azureAd*` are plain container-app env, not secrets. The only Entra ID *client
secret* in the system is the Admin UI's (a separate Key Vault secret, e.g.
`AdminUi-AzureAdClientSecret`), not wired by this Bicep.

Rotating a secret: set a new version, then
`az containerapp revision restart` so the reference re-resolves.

## Deploy commands (documentation — not run from this repo)

```bash
# 1. Resource group
az group create --name rg-ctms-dev --location westeurope

# 2. Infrastructure — fill in allowedOrigin + azureAd* first (empty ships no
#    CORS allow-list and the appsettings AzureAd fallback).
az deployment group create \
  --resource-group rg-ctms-dev \
  --template-file deploy/azure/main.bicep \
  --parameters @deploy/azure/parameters.example.json

# 3. Seed the Key Vault secrets (see above), using the deployment outputs.

# 4. Build & push the real image, then redeploy step 2 pointing apiImage at it:
az acr build --registry <acrName> --image ctms-api:<tag> .
az deployment group create ... --parameters apiImage=<acrLoginServer>/ctms-api:<tag>
```

### Outputs

`acrLoginServer`, `acrName`, `keyVaultName`, `keyVaultUri`,
`expectedSecretNames`, `apiFqdn`, `apiPrincipalId`, `apiClientId`,
`redisHostName`, `cosmosMode`, `cosmosRuDocumentEndpoint`,
`cosmosVCoreConnectionString`.

## How the image pull works

1. The container app has a **user-assigned managed identity**.
2. That identity holds **AcrPull** on the registry (role assignment in the Bicep).
3. `configuration.registries[0]` names the ACR login server and sets `identity`
   to the managed identity — no username/password, no admin user on the registry.

## Known gaps (`// TODO`)

- No VNet / private endpoints — Key Vault, Cosmos, and Redis are on public
  networking with default firewall.
- vCore Cosmos branch is a stub (compute tier / storage / HA not sized).
- No diagnostic settings on ACR / Key Vault / Redis / Cosmos.
- No custom domain or managed certificate on the ingress.
- Autoscale is a single HTTP-concurrency rule.
- The disaster-recovery / backup story for Cosmos and the Redis key ring is
  undocumented.

## Related

- [`docker.md`](docker.md) — the image and the compose posture the container app
  mirrors.
- [`azure-devops.md`](azure-devops.md) — the pipeline that calls this Bicep.
- [`deploy/azure/README.md`](../deploy/azure/README.md) — the deploy folder's own
  notes.
