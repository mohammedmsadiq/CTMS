# CTMS — Azure infrastructure

`main.bicep` is a **first-draft scaffold** for hosting CTMS on Azure Container
Apps. It compiles (`az bicep build`) but has not been deployed; treat every
`// TODO` as a real gap (networking, diagnostics, backup, throughput sizing).

## What it provisions

| Resource | Type | Notes |
|---|---|---|
| Container Registry | `Microsoft.ContainerRegistry/registries` | Basic SKU, admin user disabled. Pull is via managed identity only. |
| User-assigned managed identity | `Microsoft.ManagedIdentity/userAssignedIdentities` | Attached to the API container app. Holds the role assignments below. |
| Role assignment: **AcrPull** | on the ACR | Lets the container app pull images without registry credentials. |
| Role assignment: **Key Vault Secrets User** | on the Key Vault | Lets the container app resolve secret references at runtime. |
| Role assignment: **Key Vault Secrets Officer** | on the Key Vault | Optional; only when `kvAdminPrincipalId` is supplied, so an operator can seed secret values. |
| Key Vault | `Microsoft.KeyVault/vaults` | RBAC authorization, soft-delete on; purge protection on for `prod`. |
| Cosmos DB for MongoDB | `Microsoft.DocumentDB/databaseAccounts` (RU) **or** `Microsoft.DocumentDB/mongoClusters` (vCore) | Pick with the `cosmosMode` parameter (`ru` default). RU path is serverless and creates the `Mongo:Database` database; vCore path is a stub. |
| Azure Cache for Redis | `Microsoft.Cache/redis` | Basic C0, TLS 1.2, non-SSL port disabled. |
| Log Analytics workspace | `Microsoft.OperationalInsights/workspaces` | Backing store for Container Apps logs. |
| Container Apps environment | `Microsoft.App/managedEnvironments` | Wired to the Log Analytics workspace. |
| API container app | `Microsoft.App/containerApps` | External ingress on port **8080**, HTTP scale rule, `/health` liveness + `/health/ready` readiness probes. |

## Configuration contract

The container app sets these environment variables (ASP.NET Core config keys):

| Env var | Config key | Source |
|---|---|---|
| `ConnectionStrings__CtmsDatabase` | `ConnectionStrings:CtmsDatabase` | Key Vault secret `CtmsDatabase-ConnectionString` |
| `Mongo__Database` | `Mongo:Database` | `mongoDatabaseName` parameter (plain value) |
| `ConnectionStrings__Redis` | `ConnectionStrings:Redis` | Key Vault secret `Redis-ConnectionString` |
| `ASPNETCORE_ENVIRONMENT` | — | `Staging` for non-prod, `Production` for prod |
| `ASPNETCORE_URLS` | — | `http://+:8080` (TLS terminated at ingress) |
| `Seed__Enabled` | `Seed:Enabled` | `false` |

## Secrets expected in Key Vault

The Bicep wires **Key Vault references** — it does not create secret values.
Create them after the first deployment (they must exist before the container
app's next revision starts):

| Secret name | Value |
|---|---|
| `CtmsDatabase-ConnectionString` | Mongo connection string. RU: primary connection string from the Cosmos account (`az cosmosdb keys list --type connection-strings`). vCore: the cluster connection string with the admin password substituted in. |
| `Redis-ConnectionString` | `<redisHostName>:6380,password=<primaryKey>,ssl=True,abortConnect=False` (StackExchange.Redis format). |

```bash
az keyvault secret set --vault-name <keyVaultName> \
  --name CtmsDatabase-ConnectionString --value "<mongo-connection-string>"

az keyvault secret set --vault-name <keyVaultName> \
  --name Redis-ConnectionString --value "<host>:6380,password=<key>,ssl=True,abortConnect=False"
```

Rotating a secret: set a new version, then restart the container app revision
(`az containerapp revision restart`) so the reference re-resolves.

## How the image pull works

1. The container app has a **user-assigned managed identity**.
2. That identity holds **AcrPull** on the registry (role assignment in the Bicep).
3. The app's `configuration.registries[0]` names the ACR login server and sets
   `identity` to the managed identity resource ID — no username/password, no
   admin user on the registry.
4. Build and push the real image before pointing `apiImage` at it:

   ```bash
   az acr build --registry <acrName> --image ctms-api:<tag> .
   ```

   then redeploy with `apiImage=<acrLoginServer>/ctms-api:<tag>`.

## Deploy (documentation — do not run from this repo)

```bash
# 1. Resource group
az group create --name rg-ctms-dev --location westeurope

# 2. Infrastructure
az deployment group create \
  --resource-group rg-ctms-dev \
  --template-file deploy/azure/main.bicep \
  --parameters @deploy/azure/parameters.example.json

# 3. Seed Key Vault secrets (see above), using the deployment outputs
#    keyVaultName / redisHostName / cosmos* .

# 4. Build & push the API image, then redeploy step 2 with
#    apiImage=<acrLoginServer>/ctms-api:<tag>
```

### Outputs

`acrLoginServer`, `acrName`, `keyVaultName`, `keyVaultUri`,
`expectedSecretNames`, `apiFqdn`, `apiPrincipalId`, `apiClientId`,
`redisHostName`, `cosmosMode`, `cosmosRuDocumentEndpoint`,
`cosmosVCoreConnectionString`.

## Known gaps (`// TODO`)

- No VNet / private endpoints — Key Vault, Cosmos, and Redis are on public
  networking with default firewall.
- vCore Cosmos branch is a stub (compute tier / storage / HA not sized).
- No diagnostic settings on ACR / Key Vault / Redis / Cosmos.
- No custom domain or managed certificate on the container app ingress.
- Autoscale is a single HTTP concurrency rule.
- CI/CD wiring (who runs `az deployment group create` / `az acr build`) is
  owned by the `cicd-docs` workstream, not this file.
