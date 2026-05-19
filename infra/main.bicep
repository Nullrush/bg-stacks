targetScope = 'resourceGroup'

@description('Short environment tag')
@allowed(['dev', 'prod'])
param env string = 'prod'

@description('Azure region')
param location string = resourceGroup().location

@description('Full container image reference')
param image string

var tags = {
  project: 'bg-stacks'
  env: env
  managedBy: 'bicep'
}

var names = {
  cosmos: 'cosmos-bgstacks-${env}'
  storage: 'stbgstacks${env}'
  kv: 'kv-bgstacks-${env}'
  env: 'cae-bgstacks-${env}'
  app: 'ca-bgstacks-${env}'
  identity: 'id-bgstacks-${env}'
  workspace: 'log-bgstacks-${env}'
}

// Created first so its principalId is available to all downstream module role assignments
// without circular dependencies
resource containerAppIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: names.identity
  location: location
  tags: tags
}

// Key Vault — AVM defaults give us purgeProtection + 90-day retention for free
// Secrets User for the container app UAI is inlined here
// Cert reader for the managed environment stays standalone (circular dep: env needs KV cert → KV would need env principalId)
module keyvault 'br/public:avm/res/key-vault/vault:0.13.3' = {
  name: 'keyvault'
  params: {
    name: names.kv
    location: location
    tags: tags
    sku: 'standard'
    enableVaultForDeployment: false
    enableVaultForTemplateDeployment: false
    enableVaultForDiskEncryption: false
    roleAssignments: [
      {
        roleDefinitionIdOrName: 'Key Vault Secrets User'
        principalId: containerAppIdentity.properties.principalId
        principalType: 'ServicePrincipal'
      }
    ]
  }
}

// Existing-reader modules — no AVM equivalent; kept as-is
module wildcardCert 'modules/wildcard-cert.bicep' = {
  name: 'wildcardCert'
  params: {
    keyVaultName: keyvault.outputs.name
  }
}

module kvSecrets 'modules/keyvault-secrets.bicep' = {
  name: 'kvSecrets'
  params: {
    keyVaultName: keyvault.outputs.name
  }
}

module workspace 'br/public:avm/res/operational-insights/workspace:0.15.1' = {
  name: 'workspace'
  params: {
    name: names.workspace
    location: location
    tags: tags
    skuName: 'PerGB2018'
    dataRetention: 30
  }
}

module environment 'br/public:avm/res/app/managed-environment:0.13.3' = {
  name: 'environment'
  params: {
    name: names.env
    location: location
    tags: tags
    managedIdentities: {
      userAssignedResourceIds: [containerAppIdentity.id]
    }
    zoneRedundant: false
    publicNetworkAccess: 'Enabled'
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsWorkspaceResourceId: workspace.outputs.resourceId
    }
    dnsSuffix: 'bgstacks.com'
    certificate: {
      certificateKeyVaultProperties: {
        keyVaultUrl: wildcardCert.outputs.secretUri
        identityResourceId: containerAppIdentity.id
      }
    }
  }
}

module cosmos 'br/public:avm/res/document-db/database-account:0.19.0' = {
  name: 'cosmos'
  params: {
    name: names.cosmos
    location: location
    tags: tags
    databaseAccountOfferType: 'Standard'
    enableFreeTier: true
    defaultConsistencyLevel: 'Session'
    zoneRedundant: false
    sqlDatabases: [
      {
        name: 'bgstacks'
        containers: [
          {
            name: 'usertags'
            paths: ['/userId']
          }
          {
            name: 'events'
            paths: ['/slug']
          }
        ]
      }
    ]
    networkRestrictions: {
      publicNetworkAccess: 'Enabled'
    }
    sqlRoleAssignments: [
      {
        principalId: containerAppIdentity.properties.principalId
        roleDefinitionId: 'Cosmos DB Built-in Data Contributor'
      }
    ]
  }
}

module storage 'br/public:avm/res/storage/storage-account:0.32.0' = {
  name: 'storage'
  params: {
    name: names.storage
    location: location
    tags: tags
    skuName: 'Standard_LRS'
    kind: 'StorageV2'
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowSharedKeyAccess: false
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
    blobServices: {
      containers: [
        {
          name: 'events'
          publicAccess: 'None'
        }
      ]
    }
    roleAssignments: [
      {
        roleDefinitionIdOrName: 'Storage Blob Data Reader'
        principalId: containerAppIdentity.properties.principalId
        principalType: 'ServicePrincipal'
      }
    ]
  }
}

module containerApp 'br/public:avm/res/app/container-app:0.22.1' = {
  name: 'containerapp'
  params: {
    name: names.app
    location: location
    tags: tags
    environmentResourceId: environment.outputs.resourceId
    managedIdentities: {
      userAssignedResourceIds: [containerAppIdentity.id]
    }
    // Secrets reference the UAI for KV auth (not 'system' — the app has no system-assigned identity)
    secrets: map(kvSecrets.outputs.secretRefs, s => {
      name: s.name
      keyVaultUrl: s.keyVaultUrl
      identity: containerAppIdentity.id
    })
    ingressExternal: true
    ingressTargetPort: 8080
    ingressTransport: 'auto'
    ingressAllowInsecure: false
    containers: [
      {
        name: 'bgstacks'
        image: image
        env: [
          { name: 'ASPNETCORE_ENVIRONMENT',       value: 'Production' }
          { name: 'Cosmos__Endpoint',              value: cosmos.outputs.endpoint }
          { name: 'Cosmos__DatabaseId',            value: 'bgstacks' }
          { name: 'Blob__ServiceUri',              value: storage.outputs.primaryBlobEndpoint }
          { name: 'Events__BaseDomain',            value: 'bgstacks.com' }
          { name: 'Auth__Google__ClientId',        secretRef: 'google-client-id' }
          { name: 'Auth__Google__ClientSecret',    secretRef: 'google-client-secret' }
          { name: 'Auth__Facebook__ClientId',      secretRef: 'facebook-client-id' }
          { name: 'Auth__Facebook__ClientSecret',  secretRef: 'facebook-client-secret' }
          { name: 'Auth__Discord__ClientId',       secretRef: 'discord-client-id' }
          { name: 'Auth__Discord__ClientSecret',   secretRef: 'discord-client-secret' }
        ]
        resources: {
          cpu: json('0.25')
          memory: '0.5Gi'
        }
      }
    ]
    scaleSettings: {
      minReplicas: 0
      maxReplicas: 3
      rules: [
        {
          name: 'http-scaling'
          http: { metadata: { concurrentRequests: '10' } }
        }
      ]
    }
  }
}

output containerAppFqdn string = containerApp.outputs.fqdn
output cosmosEndpoint string = cosmos.outputs.endpoint
output blobEndpoint string = storage.outputs.primaryBlobEndpoint
output environmentStaticIp string = environment.outputs.staticIp
