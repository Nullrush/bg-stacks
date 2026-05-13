@description('Name of the Container Apps managed environment')
param environmentName string

@description('Azure region')
param location string = resourceGroup().location

@description('Custom DNS suffix for wildcard domain (e.g. bgstacks.com)')
param dnsSuffix string

@description('Resource ID of the wildcard TLS certificate in Key Vault')
param certificateKeyVaultId string

@description('Password for the PFX certificate')
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
