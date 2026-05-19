targetScope = 'resourceGroup'

@description('Short environment tag, e.g. prod or dev')
param env string = 'prod'

@description('Azure region')
param location string = resourceGroup().location

@description('Full container image reference')
param image string

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

module keyvault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    keyVaultName: 'bgstacks-${env}-kv'
    location: location
  }
}

module wildcardCert 'modules/wildcard-cert.bicep' = {
  name: 'wildcardCert'
  params: {
    keyVaultName: keyvault.outputs.keyVaultName
  }
}

module kvSecrets 'modules/keyvault-secrets.bicep' = {
  name: 'kvSecrets'
  params: {
    keyVaultName: keyvault.outputs.keyVaultName
  }
}

module environment 'modules/environment.bicep' = {
  name: 'environment'
  params: {
    environmentName: 'bgstacks-${env}-env'
    location: location
    dnsSuffix: 'bgstacks.com'
    certificateKeyVaultId: wildcardCert.outputs.secretUri
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
    oauthSecrets: kvSecrets.outputs.secretRefs
  }
}

var cosmosDataContributorRoleId = '00000000-0000-0000-0000-000000000002'

resource cosmosResource 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' existing = {
  name: cosmos.outputs.cosmosAccountName
}

resource cosmosRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  name: guid(cosmos.outputs.cosmosAccountId, containerApp.outputs.principalId, cosmosDataContributorRoleId)
  parent: cosmosResource
  properties: {
    roleDefinitionId: '${cosmos.outputs.cosmosAccountId}/sqlRoleDefinitions/${cosmosDataContributorRoleId}'
    principalId: containerApp.outputs.principalId
    scope: cosmos.outputs.cosmosAccountId
  }
}

resource storageResource 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storage.outputs.storageAccountName
}

resource blobReaderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.outputs.storageAccountId, containerApp.outputs.principalId, 'StorageBlobDataReader')
  scope: storageResource
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1')
    principalId: containerApp.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource keyVaultResource 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyvault.outputs.keyVaultName
}

resource kvSecretsUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyvault.outputs.keyVaultId, containerApp.outputs.principalId, 'KeyVaultSecretsUser')
  scope: keyVaultResource
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: containerApp.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource kvCertReaderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyvault.outputs.keyVaultId, environment.outputs.principalId, 'KeyVaultCertificateUser')
  scope: keyVaultResource
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'db79e9a7-68ee-4b58-9aeb-b90e7c24fcba')
    principalId: environment.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

output containerAppFqdn string = containerApp.outputs.fqdn
output cosmosEndpoint string = cosmos.outputs.cosmosEndpoint
output blobEndpoint string = storage.outputs.blobEndpoint
output environmentStaticIp string = environment.outputs.staticIp
