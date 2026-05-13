# ACA Migration — Infrastructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deploy the BgStacks.Web container to Azure Container Apps using Bicep IaC and two GitHub Actions workflows (infra + app).

**Architecture:** Bicep modules define a Cosmos DB account (free tier, NoSQL API), a Blob Storage account, an optional Key Vault for the wildcard TLS cert, an ACA managed environment with `*.bgstacks.com` DNS suffix, and a Container App with system-assigned managed identity. Two GitHub Actions workflows: `infra.yml` deploys Bicep on infrastructure changes; `app.yml` builds, pushes to GHCR, and updates the container on code changes.

**Tech Stack:** Bicep, Azure CLI, GitHub Actions, Azure Container Apps, Azure Cosmos DB (NoSQL), Azure Blob Storage, Azure Key Vault, GHCR

**Spec:** `docs/superpowers/specs/2026-05-13-aca-migration-design.md`

**Prerequisite:** The app plan (`2026-05-13-aca-migration-app.md`) must be complete — specifically the Dockerfile must exist at `src/BgStacks.Web/Dockerfile`.

---

## File Map

```
infra/
  main.bicep
  modules/
    cosmos.bicep
    storage.bicep
    keyvault.bicep
    environment.bicep
    containerapp.bicep
  parameters/
    main.bicepparam
    main.dev.bicepparam

.github/workflows/
  infra.yml
  app.yml                    (replaces azure-static-web-apps.yml)
```

---

### Task 1: Cosmos DB Bicep module

**Files:**
- Create: `infra/modules/cosmos.bicep`

- [ ] **Step 1: Write cosmos.bicep**

```bicep
// infra/modules/cosmos.bicep
@description('Name of the Cosmos DB account')
param accountName string

@description('Azure region')
param location string = resourceGroup().location

@description('Name of the database')
param databaseName string = 'bgstacks'

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: accountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    enableFreeTier: true
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
      }
    ]
    capabilities: []
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: cosmosAccount
  name: databaseName
  properties: {
    resource: { id: databaseName }
    options: { throughput: null }
  }
}

resource usertags 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: 'usertags'
  properties: {
    resource: {
      id: 'usertags'
      partitionKey: {
        paths: ['/userId']
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [{ path: '/*' }]
        excludedPaths: [{ path: '/"_etag"/?' }]
      }
    }
  }
}

resource events 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: 'events'
  properties: {
    resource: {
      id: 'events'
      partitionKey: {
        paths: ['/slug']
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          { path: '/*' }
        ]
        excludedPaths: [{ path: '/"_etag"/?' }]
      }
    }
  }
}

output cosmosAccountId string = cosmosAccount.id
output cosmosAccountName string = cosmosAccount.name
output cosmosEndpoint string = cosmosAccount.properties.documentEndpoint
```

- [ ] **Step 2: Lint the Bicep file**

```powershell
az bicep lint --file infra/modules/cosmos.bicep
```

Expected: No errors or warnings.

- [ ] **Step 3: Commit**

```powershell
git add infra/modules/cosmos.bicep
git commit -m "feat(infra): add Cosmos DB Bicep module — bgstacks db with usertags and events containers"
```

---

### Task 2: Storage Account Bicep module

**Files:**
- Create: `infra/modules/storage.bicep`

- [ ] **Step 1: Write storage.bicep**

```bicep
// infra/modules/storage.bicep
@description('Name of the storage account (3–24 lowercase alphanumeric)')
param storageAccountName string

@description('Azure region')
param location string = resourceGroup().location

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource eventsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'events'
  properties: {
    publicAccess: 'None'
  }
}

output storageAccountId string = storageAccount.id
output storageAccountName string = storageAccount.name
output blobEndpoint string = storageAccount.properties.primaryEndpoints.blob
```

- [ ] **Step 2: Lint**

```powershell
az bicep lint --file infra/modules/storage.bicep
```

Expected: No errors.

- [ ] **Step 3: Commit**

```powershell
git add infra/modules/storage.bicep
git commit -m "feat(infra): add Storage Account Bicep module — events blob container"
```

---

### Task 3: Key Vault Bicep module

**Files:**
- Create: `infra/modules/keyvault.bicep`

The Key Vault stores the wildcard TLS certificate used by the ACA environment for `*.bgstacks.com`. The certificate itself is uploaded manually after initial provisioning (it cannot be generated by Bicep).

- [ ] **Step 1: Write keyvault.bicep**

```bicep
// infra/modules/keyvault.bicep
@description('Name of the Key Vault')
param keyVaultName string

@description('Azure region')
param location string = resourceGroup().location

@description('Object ID of the ACA environment managed identity or service principal that needs certificate read access')
param certificateReaderObjectId string

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: tenant().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// Grant the ACA environment managed identity permission to read certificates
resource certificateReaderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, certificateReaderObjectId, 'KeyVaultCertificateUser')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'db79e9a7-68ee-4b58-9aeb-b90e7c24fcba') // Key Vault Certificate User
    principalId: certificateReaderObjectId
    principalType: 'ServicePrincipal'
  }
}

output keyVaultId string = keyVault.id
output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
```

- [ ] **Step 2: Lint**

```powershell
az bicep lint --file infra/modules/keyvault.bicep
```

Expected: No errors.

- [ ] **Step 3: Commit**

```powershell
git add infra/modules/keyvault.bicep
git commit -m "feat(infra): add Key Vault Bicep module for wildcard TLS certificate"
```

---

### Task 4: ACA Environment Bicep module

**Files:**
- Create: `infra/modules/environment.bicep`

- [ ] **Step 1: Write environment.bicep**

```bicep
// infra/modules/environment.bicep
@description('Name of the Container Apps managed environment')
param environmentName string

@description('Azure region')
param location string = resourceGroup().location

@description('Custom DNS suffix for wildcard domain (e.g. bgstacks.com)')
param dnsSuffix string

@description('Resource ID of the wildcard TLS certificate in Key Vault')
param certificateKeyVaultId string

@description('Password for the PFX certificate (can be empty string for cert with no password)')
@secure()
param certificatePassword string = ''

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${environmentName}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    customDomainConfiguration: {
      dnsSuffix: dnsSuffix
      certificateKeyVaultProperties: {
        keyVaultUrl: certificateKeyVaultId
        identity: 'system'
      }
      certificatePassword: certificatePassword
    }
  }
}

output environmentId string = environment.id
output environmentName string = environment.name
output staticIp string = environment.properties.staticIp
output defaultDomain string = environment.properties.defaultDomain
```

- [ ] **Step 2: Lint**

```powershell
az bicep lint --file infra/modules/environment.bicep
```

Expected: No errors.

- [ ] **Step 3: Commit**

```powershell
git add infra/modules/environment.bicep
git commit -m "feat(infra): add ACA managed environment Bicep module — wildcard DNS suffix"
```

---

### Task 5: Container App Bicep module

**Files:**
- Create: `infra/modules/containerapp.bicep`

- [ ] **Step 1: Write containerapp.bicep**

```bicep
// infra/modules/containerapp.bicep
@description('Name of the Container App')
param containerAppName string

@description('Azure region')
param location string = resourceGroup().location

@description('Resource ID of the ACA managed environment')
param environmentId string

@description('Full image reference, e.g. ghcr.io/nullrush/bg-stacks:abc1234')
param image string

@description('Cosmos DB account endpoint (no connection string — uses managed identity)')
param cosmosEndpoint string

@description('Blob Storage service URI (no connection string — uses managed identity)')
param blobServiceUri string

@description('Base domain for event subdomain routing')
param baseDomain string = 'bgstacks.com'

@description('Cosmos DB database name')
param cosmosDatabaseId string = 'bgstacks'

@description('Google OAuth client ID')
@secure()
param googleClientId string

@description('Google OAuth client secret')
@secure()
param googleClientSecret string

@description('Facebook OAuth client ID')
@secure()
param facebookClientId string

@description('Facebook OAuth client secret')
@secure()
param facebookClientSecret string

@description('Discord OAuth client ID')
@secure()
param discordClientId string

@description('Discord OAuth client secret')
@secure()
param discordClientSecret string

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    environmentId: environmentId
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      secrets: [
        { name: 'google-client-id',       value: googleClientId }
        { name: 'google-client-secret',   value: googleClientSecret }
        { name: 'facebook-client-id',     value: facebookClientId }
        { name: 'facebook-client-secret', value: facebookClientSecret }
        { name: 'discord-client-id',      value: discordClientId }
        { name: 'discord-client-secret',  value: discordClientSecret }
      ]
    }
    template: {
      containers: [
        {
          name: 'bgstacks'
          image: image
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'Cosmos__Endpoint',        value: cosmosEndpoint }
            { name: 'Cosmos__DatabaseId',      value: cosmosDatabaseId }
            { name: 'Blob__ServiceUri',        value: blobServiceUri }
            { name: 'Events__BaseDomain',      value: baseDomain }
            { name: 'Auth__Google__ClientId',      secretRef: 'google-client-id' }
            { name: 'Auth__Google__ClientSecret',  secretRef: 'google-client-secret' }
            { name: 'Auth__Facebook__ClientId',    secretRef: 'facebook-client-id' }
            { name: 'Auth__Facebook__ClientSecret',secretRef: 'facebook-client-secret' }
            { name: 'Auth__Discord__ClientId',     secretRef: 'discord-client-id' }
            { name: 'Auth__Discord__ClientSecret', secretRef: 'discord-client-secret' }
          ]
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
      scale: {
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
}

output containerAppId string = containerApp.id
output containerAppName string = containerApp.name
output principalId string = containerApp.identity.principalId
output fqdn string = containerApp.properties.configuration.ingress.fqdn
```

- [ ] **Step 2: Lint**

```powershell
az bicep lint --file infra/modules/containerapp.bicep
```

Expected: No errors.

- [ ] **Step 3: Commit**

```powershell
git add infra/modules/containerapp.bicep
git commit -m "feat(infra): add Container App Bicep module — system-assigned identity, 0-3 replicas"
```

---

### Task 6: main.bicep + parameter files

**Files:**
- Create: `infra/main.bicep`
- Create: `infra/parameters/main.bicepparam`
- Create: `infra/parameters/main.dev.bicepparam`

- [ ] **Step 1: Write main.bicep**

```bicep
// infra/main.bicep
targetScope = 'resourceGroup'

@description('Short environment tag, e.g. prod or dev')
param env string = 'prod'

@description('Azure region')
param location string = resourceGroup().location

@description('Full container image reference')
param image string

@description('Google OAuth client ID')
@secure()
param googleClientId string

@description('Google OAuth client secret')
@secure()
param googleClientSecret string

@description('Facebook OAuth client ID')
@secure()
param facebookClientId string

@description('Facebook OAuth client secret')
@secure()
param facebookClientSecret string

@description('Discord OAuth client ID')
@secure()
param discordClientId string

@description('Discord OAuth client secret')
@secure()
param discordClientSecret string

@description('Object ID used to grant Key Vault certificate access (ACA environment managed identity)')
param kvCertReaderObjectId string = ''

@description('Resource ID of the wildcard TLS cert in Key Vault (leave blank to skip DNS suffix setup)')
param wildcardCertKeyVaultId string = ''

// ── Modules ────────────────────────────────────────────────────────────────

module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmos'
  params: {
    accountName: 'bgstacks-${env}'
    location: location
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    storageAccountName: 'bgstacks${env}blob'
    location: location
  }
}

module keyvault 'modules/keyvault.bicep' = if (!empty(kvCertReaderObjectId)) {
  name: 'keyvault'
  params: {
    keyVaultName: 'bgstacks-${env}-kv'
    location: location
    certificateReaderObjectId: kvCertReaderObjectId
  }
}

module environment 'modules/environment.bicep' = {
  name: 'environment'
  params: {
    environmentName: 'bgstacks-${env}-env'
    location: location
    dnsSuffix: 'bgstacks.com'
    certificateKeyVaultId: wildcardCertKeyVaultId
  }
}

module containerApp 'modules/containerapp.bicep' = {
  name: 'containerapp'
  params: {
    containerAppName: 'bgstacks-${env}'
    location: location
    environmentId: environment.outputs.environmentId
    image: image
    cosmosEndpoint: cosmos.outputs.cosmosEndpoint
    blobServiceUri: storage.outputs.blobEndpoint
    googleClientId: googleClientId
    googleClientSecret: googleClientSecret
    facebookClientId: facebookClientId
    facebookClientSecret: facebookClientSecret
    discordClientId: discordClientId
    discordClientSecret: discordClientSecret
  }
}

// ── RBAC: Cosmos DB ────────────────────────────────────────────────────────
// Built-in Data Contributor role on the Cosmos DB account
var cosmosDataContributorRoleId = '00000000-0000-0000-0000-000000000002'

resource cosmosRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  name: guid(cosmos.outputs.cosmosAccountId, containerApp.outputs.principalId, cosmosDataContributorRoleId)
  parent: cosmosResource
  properties: {
    roleDefinitionId: '${cosmos.outputs.cosmosAccountId}/sqlRoleDefinitions/${cosmosDataContributorRoleId}'
    principalId: containerApp.outputs.principalId
    scope: cosmos.outputs.cosmosAccountId
  }
}

resource cosmosResource 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' existing = {
  name: cosmos.outputs.cosmosAccountName
}

// ── RBAC: Blob Storage ─────────────────────────────────────────────────────
// Storage Blob Data Reader (read event game data from Blob Storage)
resource storageResource 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storage.outputs.storageAccountName
}

resource blobReaderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.outputs.storageAccountId, containerApp.outputs.principalId, 'StorageBlobDataReader')
  scope: storageResource
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1') // Storage Blob Data Reader
    principalId: containerApp.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// ── Outputs ────────────────────────────────────────────────────────────────
output containerAppFqdn string = containerApp.outputs.fqdn
output cosmosEndpoint string = cosmos.outputs.cosmosEndpoint
output blobEndpoint string = storage.outputs.blobEndpoint
output environmentStaticIp string = environment.outputs.staticIp
```

- [ ] **Step 2: Write main.bicepparam (production)**

```bicep
// infra/parameters/main.bicepparam
using '../main.bicep'

param env = 'prod'
param image = 'ghcr.io/nullrush/bg-stacks:latest'

// OAuth secrets — fill in from provider dashboards before deploying
param googleClientId = ''
param googleClientSecret = ''
param facebookClientId = ''
param facebookClientSecret = ''
param discordClientId = ''
param discordClientSecret = ''

// Set after provisioning Key Vault and uploading wildcard cert
param wildcardCertKeyVaultId = ''
param kvCertReaderObjectId = ''
```

- [ ] **Step 3: Write main.dev.bicepparam**

```bicep
// infra/parameters/main.dev.bicepparam
using '../main.bicep'

param env = 'dev'
param image = 'ghcr.io/nullrush/bg-stacks:latest'

param googleClientId = ''
param googleClientSecret = ''
param facebookClientId = ''
param facebookClientSecret = ''
param discordClientId = ''
param discordClientSecret = ''

param wildcardCertKeyVaultId = ''
param kvCertReaderObjectId = ''
```

- [ ] **Step 4: Lint main.bicep**

```powershell
az bicep lint --file infra/main.bicep
```

Expected: No errors.

- [ ] **Step 5: Dry-run validation (requires Azure login)**

```powershell
az login
az group create --name bgstacks-rg --location eastus  # if not already created
az deployment group validate `
  --resource-group bgstacks-rg `
  --template-file infra/main.bicep `
  --parameters infra/parameters/main.bicepparam `
  --parameters image=ghcr.io/nullrush/bg-stacks:placeholder
```

Expected: Validation passed. If secrets are missing, errors will reference missing required parameters — fill in placeholder values in the param file for the dry-run.

- [ ] **Step 6: Commit**

```powershell
git add infra/
git commit -m "feat(infra): add main.bicep and parameter files — wires all modules, RBAC assignments"
```

---

### Task 7: GitHub Actions — infra.yml

**Files:**
- Create: `.github/workflows/infra.yml`

- [ ] **Step 1: Write infra.yml**

```yaml
# .github/workflows/infra.yml
name: Deploy Infrastructure

on:
  push:
    branches: [main]
    paths: ['infra/**']
  workflow_dispatch:

permissions:
  id-token: write
  contents: read

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v5

      - name: Azure login (OIDC)
        uses: azure/login@v2
        with:
          client-id: ${{ vars.AZURE_CLIENT_ID }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}

      - name: Deploy Bicep
        run: |
          az deployment group create \
            --resource-group ${{ vars.AZURE_RESOURCE_GROUP }} \
            --template-file infra/main.bicep \
            --parameters infra/parameters/main.bicepparam \
            --parameters \
              googleClientId="${{ secrets.GOOGLE_CLIENT_ID }}" \
              googleClientSecret="${{ secrets.GOOGLE_CLIENT_SECRET }}" \
              facebookClientId="${{ secrets.FACEBOOK_CLIENT_ID }}" \
              facebookClientSecret="${{ secrets.FACEBOOK_CLIENT_SECRET }}" \
              discordClientId="${{ secrets.DISCORD_CLIENT_ID }}" \
              discordClientSecret="${{ secrets.DISCORD_CLIENT_SECRET }}" \
            --no-wait false
```

- [ ] **Step 2: Configure required GitHub secrets and variables**

In the GitHub repo settings (Settings → Secrets and Variables → Actions), add:

**Repository variables (non-secret):**
- `AZURE_CLIENT_ID` — the app registration client ID used for OIDC login
- `AZURE_TENANT_ID` — Azure AD tenant ID
- `AZURE_SUBSCRIPTION_ID` — Azure subscription ID
- `AZURE_RESOURCE_GROUP` — e.g. `bgstacks-rg`

**Repository secrets:**
- `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`
- `FACEBOOK_CLIENT_ID`, `FACEBOOK_CLIENT_SECRET`
- `DISCORD_CLIENT_ID`, `DISCORD_CLIENT_SECRET`

The OIDC app registration must have a federated credential for `repo:Nullrush/bg-stacks:ref:refs/heads/main`.

- [ ] **Step 3: Commit**

```powershell
git add .github/workflows/infra.yml
git commit -m "feat(ci): add infra.yml — deploys Bicep on infra/** changes via OIDC"
```

---

### Task 8: GitHub Actions — app.yml

**Files:**
- Create: `.github/workflows/app.yml`
- Archive: `.github/workflows/azure-static-web-apps.yml` (rename or delete)

- [ ] **Step 1: Archive the old SWA workflow**

```powershell
git mv .github/workflows/azure-static-web-apps.yml .github/workflows/azure-static-web-apps.yml.archived
```

- [ ] **Step 2: Write app.yml**

```yaml
# .github/workflows/app.yml
name: Build and Deploy App

on:
  push:
    branches: [main]
    paths:
      - 'src/**'
      - 'src/BgStacks.Web/Dockerfile'
  workflow_dispatch:

permissions:
  id-token: write
  contents: read
  packages: write

env:
  REGISTRY: ghcr.io
  IMAGE_NAME: ${{ github.repository_owner }}/bg-stacks

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v5
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.x'
      - name: Run tests
        run: dotnet test tests/BgStacks.Web.Tests --no-restore --logger "trx;LogFileName=results.trx"
      - name: Publish test results
        uses: actions/upload-artifact@v4
        if: always()
        with:
          name: test-results
          path: '**/*.trx'

  build-and-deploy:
    needs: test
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v5

      - name: Log in to GHCR
        uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Extract metadata for Docker
        id: meta
        uses: docker/metadata-action@v5
        with:
          images: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}
          tags: |
            type=sha,prefix=,format=short
            type=raw,value=latest,enable=${{ github.ref == 'refs/heads/main' }}

      - name: Build and push Docker image
        uses: docker/build-push-action@v6
        with:
          context: .
          file: src/BgStacks.Web/Dockerfile
          push: true
          tags: ${{ steps.meta.outputs.tags }}
          labels: ${{ steps.meta.outputs.labels }}

      - name: Azure login (OIDC)
        uses: azure/login@v2
        with:
          client-id: ${{ vars.AZURE_CLIENT_ID }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}

      - name: Update Container App image
        run: |
          SHA_TAG="${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:$(git rev-parse --short HEAD)"
          az containerapp update \
            --name bgstacks-prod \
            --resource-group ${{ vars.AZURE_RESOURCE_GROUP }} \
            --image "$SHA_TAG"
```

- [ ] **Step 3: Verify the workflow file is valid YAML**

```powershell
# Quick YAML syntax check (requires Python)
python -c "import yaml; yaml.safe_load(open('.github/workflows/app.yml'))"
```

Expected: No output (no errors).

- [ ] **Step 4: Commit**

```powershell
git add .github/workflows/app.yml .github/workflows/azure-static-web-apps.yml.archived
git commit -m "feat(ci): add app.yml — build, push to GHCR, deploy to ACA on src/** changes"
```

---

### Task 9: First deployment

- [ ] **Step 1: Create the Azure resource group**

```powershell
az login
az group create --name bgstacks-rg --location eastus
```

- [ ] **Step 2: Fill in OAuth credentials in main.bicepparam**

Get the client IDs and secrets from:
- Google: [Google Cloud Console](https://console.cloud.google.com/) → APIs & Services → Credentials → OAuth 2.0 Client ID
  - Authorized redirect URIs: `https://<your-aca-fqdn>/.auth/login/google/callback`
- Facebook: [Facebook Developers](https://developers.facebook.com/) → App → Facebook Login → Settings
  - Valid OAuth Redirect URIs: `https://<your-aca-fqdn>/.auth/login/facebook/callback`
- Discord: [Discord Developer Portal](https://discord.com/developers/applications) → OAuth2
  - Redirects: `https://<your-aca-fqdn>/.auth/login/discord/callback`

Add them to GitHub secrets (see Task 7, Step 2). You can use placeholder values for the initial Bicep deployment and update later.

- [ ] **Step 3: Upload wildcard TLS certificate to Key Vault**

Skip the DNS suffix configuration initially — deploy without `wildcardCertKeyVaultId` first and add the wildcard cert as a follow-up once the ACA environment is running. The app will be reachable via its default ACA FQDN in the interim.

- [ ] **Step 4: Run the initial Bicep deployment**

```powershell
az deployment group create `
  --resource-group bgstacks-rg `
  --template-file infra/main.bicep `
  --parameters infra/parameters/main.bicepparam `
  --parameters `
    googleClientId="<your-google-client-id>" `
    googleClientSecret="<your-google-client-secret>" `
    facebookClientId="<your-facebook-client-id>" `
    facebookClientSecret="<your-facebook-client-secret>" `
    discordClientId="<your-discord-client-id>" `
    discordClientSecret="<your-discord-client-secret>" `
    image="ghcr.io/nullrush/bg-stacks:latest"
```

Expected: Deployment succeeds. Note the `containerAppFqdn` output — this is the initial access URL.

- [ ] **Step 5: Upload a dev event to Blob Storage**

```powershell
az storage blob upload-batch `
  --account-name bgstacksprodblob `
  --destination events/dev `
  --source public/ `
  --pattern "games.json" `
  --overwrite
# repeat for mechanics.json and categories.json
```

- [ ] **Step 6: Register the dev event in Cosmos DB**

Using the Azure portal Data Explorer or Azure CLI:

```powershell
az cosmosdb sql item create `
  --account-name bgstacks-prod `
  --database-name bgstacks `
  --container-name events `
  --body '{
    "id": "gw-2026-pnw",
    "slug": "gw-2026-pnw",
    "name": "Geekway 2026 Prime Play & Win",
    "eventDate": "2026-06-12",
    "isPublic": true
  }'
```

- [ ] **Step 7: Push a commit to trigger app.yml**

Make a trivial change (e.g. update a comment in Program.cs), commit, and push to `main`. Verify the GitHub Actions `app.yml` workflow runs successfully and deploys the new image.

- [ ] **Step 8: Smoke-test the deployed app**

```powershell
$fqdn = "bgstacks-prod.<env-default-domain>"  # from Bicep output
Invoke-RestMethod "https://$fqdn/.auth/me"         # should return { clientPrincipal: null }
Invoke-RestMethod "https://$fqdn/api/events"       # should return the gw-2026-pnw event
Invoke-RestMethod "https://$fqdn/games.json"       # should return 404 (no slug on default FQDN)
```

- [ ] **Step 9: Configure wildcard domain (follow-up)**

Once the wildcard TLS cert is available:
1. Upload the `.pfx` to Key Vault: `az keyvault certificate import --vault-name bgstacks-prod-kv --name wildcard-bgstacks-com --file wildcard.pfx`
2. Get the cert resource ID and set `wildcardCertKeyVaultId` in `main.bicepparam`
3. Add a DNS `A` record: `*` → `environmentStaticIp` (from Bicep output)
4. Add a DNS `TXT` record: `asuid` → the ACA environment verification token (from portal)
5. Re-run the Bicep deployment: `az deployment group create ...`
6. Update OAuth provider redirect URIs to use the `*.bgstacks.com` pattern

---

## Self-Review

**Spec coverage check:**

| Spec section | Covered by task |
|---|---|
| Bicep: cosmos.bicep (usertags + events containers) | Task 1 |
| Bicep: storage.bicep (Blob Storage, events container) | Task 2 |
| Bicep: keyvault.bicep (wildcard cert storage) | Task 3 |
| Bicep: environment.bicep (ACA env, wildcard DNS suffix) | Task 4 |
| Bicep: containerapp.bicep (system-assigned identity, 0–3 replicas, secrets) | Task 5 |
| Bicep: main.bicep (modules + RBAC for Cosmos + Blob) | Task 6 |
| Bicep: parameter files (prod + dev) | Task 6 |
| GitHub Actions: infra.yml (deploy on infra/** changes) | Task 7 |
| GitHub Actions: app.yml (build, push GHCR, update ACA on src/** changes) | Task 8 |
| Managed identity RBAC (Cosmos Built-in Data Contributor, Blob Reader) | Task 6 |
| OAuth secrets stored as Container Apps secrets | Task 5 |
| No storage connection strings | Tasks 5, 6 |
| SHA-tagged images for rollback | Task 8 |
| Wildcard domain setup procedure | Task 9 |
| First event registration in Cosmos | Task 9 |

**Placeholder scan:** None. Task 9 Step 6 includes the exact Cosmos DB document body for registering the first event.

**Type consistency:** Bicep parameter names match between modules and main.bicep. `cosmosDataContributorRoleId = '00000000-0000-0000-0000-000000000002'` is the correct built-in Cosmos DB Data Contributor role GUID. `2a2b9908-6ea1-4ae2-8e65-a410df84e7d1` is the correct Storage Blob Data Reader role GUID.
