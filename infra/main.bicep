targetScope = 'resourceGroup'

@description('Short environment tag, e.g. prod or dev')
param env string = 'prod'

@description('Azure region')
param location string = resourceGroup().location

@description('Full container image reference')
param image string

@secure()
param googleClientId string

@secure()
param googleClientSecret string

@secure()
param facebookClientId string

@secure()
param facebookClientSecret string

@secure()
param discordClientId string

@secure()
param discordClientSecret string

@description('Object ID used to grant Key Vault certificate access')
param kvCertReaderObjectId string = ''

@description('Resource ID of the wildcard TLS cert in Key Vault (leave blank to skip DNS suffix)')
param wildcardCertKeyVaultId string = ''

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

output containerAppFqdn string = containerApp.outputs.fqdn
output cosmosEndpoint string = cosmos.outputs.cosmosEndpoint
output blobEndpoint string = storage.outputs.blobEndpoint
output environmentStaticIp string = environment.outputs.staticIp
