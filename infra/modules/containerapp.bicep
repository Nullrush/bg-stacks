@description('Name of the Container App')
param containerAppName string

@description('Azure region')
param location string = resourceGroup().location

@description('Resource ID of the ACA managed environment')
param environmentId string

@description('Full image reference, e.g. ghcr.io/nullrush/bg-stacks:abc1234')
param image string

@description('Cosmos DB account endpoint')
param cosmosEndpoint string

@description('Blob Storage service URI')
param blobServiceUri string

@description('Base domain for event subdomain routing')
param baseDomain string = 'bgstacks.com'

@description('Cosmos DB database name')
param cosmosDatabaseId string = 'bgstacks'

@description('Array of Container App secret definitions ({ name, keyVaultUrl, identity }) for OAuth credentials')
param oauthSecrets array

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
      secrets: oauthSecrets
    }
    template: {
      containers: [
        {
          name: 'bgstacks'
          image: image
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT',          value: 'Production' }
            { name: 'Cosmos__Endpoint',                value: cosmosEndpoint }
            { name: 'Cosmos__DatabaseId',              value: cosmosDatabaseId }
            { name: 'Blob__ServiceUri',                value: blobServiceUri }
            { name: 'Events__BaseDomain',              value: baseDomain }
            { name: 'Auth__Google__ClientId',          secretRef: 'google-client-id' }
            { name: 'Auth__Google__ClientSecret',      secretRef: 'google-client-secret' }
            { name: 'Auth__Facebook__ClientId',        secretRef: 'facebook-client-id' }
            { name: 'Auth__Facebook__ClientSecret',    secretRef: 'facebook-client-secret' }
            { name: 'Auth__Discord__ClientId',         secretRef: 'discord-client-id' }
            { name: 'Auth__Discord__ClientSecret',     secretRef: 'discord-client-secret' }
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
