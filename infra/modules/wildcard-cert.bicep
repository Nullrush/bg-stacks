@description('Name of the Key Vault containing the wildcard certificate')
param keyVaultName string

resource kv 'Microsoft.KeyVault/vaults@2025-05-01' existing = {
  name: keyVaultName
}

resource wildcardCert 'Microsoft.KeyVault/vaults/secrets@2025-05-01' existing = {
  parent: kv
  name: 'wildcard-bgstacks-com'
}

output secretUri string = wildcardCert.properties.secretUri
