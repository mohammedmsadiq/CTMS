// =============================================================================
// CTMS — Azure infrastructure (first draft).
//
// Scope: resource group.  Deploy (documentation only — do NOT run here):
//
//   az deployment group create \
//     --resource-group rg-ctms-dev \
//     --template-file deploy/azure/main.bicep \
//     --parameters @deploy/azure/parameters.example.json
//
// Provisions: ACR, Container Apps environment + API container app,
// Cosmos DB for MongoDB (RU or vCore), Azure Cache for Redis (Basic),
// Key Vault, and a user-assigned managed identity with AcrPull +
// Key Vault Secrets User role assignments.
//
// TODO: this is a scaffold. Review network isolation (private endpoints /
// VNet), diagnostic settings, autoscale rules, and Cosmos throughput before
// anything past dev.
// =============================================================================

targetScope = 'resourceGroup'

// ----- parameters ------------------------------------------------------------

@description('Short name stem for all resources, e.g. "ctms".')
@minLength(3)
@maxLength(12)
param namePrefix string = 'ctms'

@description('Environment discriminator: dev | test | prod.')
@allowed([ 'dev', 'test', 'prod' ])
param environmentName string = 'dev'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Container image reference the API container app runs. Push to ACR first; until then this points at a public placeholder.')
param apiImage string = 'mcr.microsoft.com/dotnet/samples:aspnetapp'

@description('Cosmos DB for MongoDB flavour: "ru" (serverless request-units account) or "vcore" (MongoDB vCore cluster).')
@allowed([ 'ru', 'vcore' ])
param cosmosMode string = 'ru'

@description('Mongo database name the API uses (config key Mongo:Database).')
param mongoDatabaseName string = 'ctms'

@description('Admin username for the Cosmos MongoDB vCore cluster (only used when cosmosMode == "vcore").')
param mongoVCoreAdminUser string = 'ctmsadmin'

@description('Admin password for the Cosmos MongoDB vCore cluster (only used when cosmosMode == "vcore"). Supply at deploy time via a secure parameter or Key Vault reference — never commit it.')
@secure()
param mongoVCoreAdminPassword string = ''

@description('Entra object ID of a user/group to grant Key Vault Secrets Officer (so an operator can seed the connection-string values). Leave empty to skip.')
param kvAdminPrincipalId string = ''

@description('Single allowed CORS origin for the API (config key Cors:AllowedOrigins[0]) — the browser SDK / CDN delivery path and the Admin UI origin. Empty = the API allows no cross-origin request.')
param allowedOrigin string = ''

@description('Entra ID authority instance the API validates bearer tokens against (config key AzureAd:Instance). Override for sovereign clouds.')
#disable-next-line no-hardcoded-env-urls // Entra ID global-cloud authority; sovereign-cloud deployments pass their own.
param azureAdInstance string = 'https://login.microsoftonline.com/'

@description('Entra ID tenant (directory) ID the API validates access tokens against (config key AzureAd:TenantId). Empty leaves the AzureAd env unset so the app falls back to appsettings.')
param azureAdTenantId string = ''

@description('Entra ID application (client) ID of the API app registration (config key AzureAd:ClientId).')
param azureAdClientId string = ''

@description('Accepted audience for incoming access tokens, e.g. "api://ctms" (config key AzureAd:Audience).')
param azureAdAudience string = ''

// ----- naming --------------------------------------------------------------

var suffix = uniqueString(resourceGroup().id, namePrefix, environmentName)
var baseName = '${namePrefix}-${environmentName}'
var acrName = toLower(replace('${namePrefix}${environmentName}acr${suffix}', '-', ''))
var kvName = take(toLower('${namePrefix}${environmentName}kv${suffix}'), 24)
var redisName = '${baseName}-redis-${suffix}'
var cosmosRuName = toLower('${baseName}-cosmos-${suffix}')
var cosmosVCoreName = toLower('${baseName}-mongo-${suffix}')
var logName = '${baseName}-logs'
var acaEnvName = '${baseName}-aca-env'
var apiAppName = '${baseName}-api'
var uamiName = '${baseName}-api-id'

// Well-known built-in role definition IDs.
var roleAcrPull = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
var roleKeyVaultSecretsUser = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
var roleKeyVaultSecretsOfficer = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')

// Expected Key Vault secret names (see deploy/azure/README.md).
var secretNameMongo = 'CtmsDatabase-ConnectionString'
var secretNameRedis = 'Redis-ConnectionString'

// Optional container-app env fragments, concatenated into the API container's
// env below. Both are plain (non-secret) values.
//   - CORS: one origin, only emitted when a value is supplied (an empty origin
//     would otherwise be a meaningless allow-list entry).
//   - AzureAd: the API is a bearer-token VALIDATOR — it needs tenant id, client
//     id and audience but NO client secret. The confidential-client secret is
//     the Admin UI's (Key Vault secret 'AdminUi-AzureAdClientSecret'), not the
//     API's. Emitted only when a tenant id is supplied; otherwise the image
//     falls back to its appsettings AzureAd section.
var corsEnv = empty(allowedOrigin) ? [] : [
  {
    name: 'Cors__AllowedOrigins__0'
    value: allowedOrigin
  }
]

var azureAdEnv = empty(azureAdTenantId) ? [] : [
  {
    name: 'AzureAd__Instance'
    value: azureAdInstance
  }
  {
    name: 'AzureAd__TenantId'
    value: azureAdTenantId
  }
  {
    name: 'AzureAd__ClientId'
    value: azureAdClientId
  }
  {
    name: 'AzureAd__Audience'
    value: azureAdAudience
  }
]

// ----- user-assigned managed identity --------------------------------------

resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: uamiName
  location: location
}

// ----- container registry ------------------------------------------------------

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
  }
}

resource acrPullAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, uami.id, roleAcrPull)
  scope: acr
  properties: {
    principalId: uami.properties.principalId
    roleDefinitionId: roleAcrPull
    principalType: 'ServicePrincipal'
  }
}

// ----- key vault -------------------------------------------------------------

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: kvName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: tenant().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    enablePurgeProtection: environmentName == 'prod' ? true : null
    publicNetworkAccess: 'Enabled' // TODO: lock down to private endpoint / trusted services.
  }
}

resource kvSecretsUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, uami.id, roleKeyVaultSecretsUser)
  scope: kv
  properties: {
    principalId: uami.properties.principalId
    roleDefinitionId: roleKeyVaultSecretsUser
    principalType: 'ServicePrincipal'
  }
}

resource kvSecretsOfficerAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(kvAdminPrincipalId)) {
  name: guid(kv.id, kvAdminPrincipalId, roleKeyVaultSecretsOfficer)
  scope: kv
  properties: {
    principalId: kvAdminPrincipalId
    roleDefinitionId: roleKeyVaultSecretsOfficer
  }
}

// ----- Cosmos DB for MongoDB (RU account) ---------------------------------

resource cosmosRu 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = if (cosmosMode == 'ru') {
  name: cosmosRuName
  location: location
  kind: 'MongoDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    apiProperties: {
      serverVersion: '7.0'
    }
    capabilities: [
      { name: 'EnableMongo' }
      { name: 'EnableServerless' }
    ]
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    disableKeyBasedMetadataWriteAccess: true
  }
}

resource cosmosRuDb 'Microsoft.DocumentDB/databaseAccounts/mongodbDatabases@2024-05-15' = if (cosmosMode == 'ru') {
  parent: cosmosRu
  name: mongoDatabaseName
  properties: {
    resource: {
      id: mongoDatabaseName
    }
  }
}

// ----- Cosmos DB for MongoDB (vCore cluster) -----------------------------------
// TODO: vCore support is a stub. Fill in storage/compute tier, HA, and
// networking to match the target workload before using cosmosMode == 'vcore'.

resource cosmosVCore 'Microsoft.DocumentDB/mongoClusters@2024-07-01' = if (cosmosMode == 'vcore') {
  name: cosmosVCoreName
  location: location
  properties: {
    administrator: {
      userName: mongoVCoreAdminUser
      password: mongoVCoreAdminPassword
    }
    serverVersion: '7.0'
    compute: {
      tier: 'M30'
    }
    storage: {
      sizeGb: 32
    }
    sharding: {
      shardCount: 1
    }
    highAvailability: {
      targetMode: environmentName == 'prod' ? 'ZoneRedundantPreferred' : 'Disabled'
    }
  }
}

// ----- Azure Cache for Redis (Basic) ----------------------------------------

resource redis 'Microsoft.Cache/redis@2024-03-01' = {
  name: redisName
  location: location
  properties: {
    sku: {
      name: 'Basic'
      family: 'C'
      capacity: 0
    }
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
    redisVersion: '6'
  }
}

// ----- observability: Log Analytics ------------------------------------------

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// ----- Container Apps environment ------------------------------------------

resource acaEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: acaEnvName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

// ----- API container app --------------------------------------------------------
// Secrets are pulled from Key Vault at runtime via the user-assigned identity.
// The connection-string secrets must exist in the vault BEFORE this app starts
// (see deploy/azure/README.md). They are wired here as Key Vault references.

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiAppName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${uami.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: acaEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: acr.properties.loginServer
          identity: uami.id
        }
      ]
      secrets: [
        {
          name: 'ctms-database-connection-string'
          keyVaultUrl: '${kv.properties.vaultUri}secrets/${secretNameMongo}'
          identity: uami.id
        }
        {
          name: 'redis-connection-string'
          keyVaultUrl: '${kv.properties.vaultUri}secrets/${secretNameRedis}'
          identity: uami.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          // Base env + optional CORS/AzureAd fragments (see the corsEnv/azureAdEnv
          // vars). Auth itself is ON here by the image's appsettings default
          // (Auth:Enabled=true) since ASPNETCORE_ENVIRONMENT is Staging/Production.
          env: concat([
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: environmentName == 'prod' ? 'Production' : 'Staging'
            }
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:8080'
            }
            {
              name: 'ConnectionStrings__CtmsDatabase'
              secretRef: 'ctms-database-connection-string'
            }
            {
              name: 'Mongo__Database'
              value: mongoDatabaseName
            }
            {
              // Bundle cache AND the Data Protection key ring (config key
              // ConnectionStrings:Redis) — the API persists its key ring here so
              // every replica shares one set of keys.
              name: 'ConnectionStrings__Redis'
              secretRef: 'redis-connection-string'
            }
            {
              name: 'Seed__Enabled'
              value: 'false'
            }
            {
              name: 'RateLimit__Enabled'
              value: 'true'
            }
          ], corsEnv, azureAdEnv)
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
              }
              initialDelaySeconds: 15
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 5
        rules: [
          {
            name: 'http-scale'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
  dependsOn: [
    acrPullAssignment
    kvSecretsUserAssignment
  ]
}

// ----- outputs -------------------------------------------------------------

output acrLoginServer string = acr.properties.loginServer
output acrName string = acr.name
output keyVaultName string = kv.name
output keyVaultUri string = kv.properties.vaultUri
output expectedSecretNames array = [ secretNameMongo, secretNameRedis ]
output apiFqdn string = apiApp.properties.configuration.ingress.fqdn
output apiPrincipalId string = uami.properties.principalId
output apiClientId string = uami.properties.clientId
output redisHostName string = redis.properties.hostName
output cosmosMode string = cosmosMode
output cosmosRuDocumentEndpoint string = cosmosMode == 'ru' ? cosmosRu!.properties.documentEndpoint : ''
output cosmosVCoreConnectionString string = cosmosMode == 'vcore' ? cosmosVCore!.properties.connectionString : ''
