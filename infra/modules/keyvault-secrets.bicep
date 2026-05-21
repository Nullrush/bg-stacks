@description('Name of the Key Vault containing OAuth credentials')
param keyVaultName string

resource kv 'Microsoft.KeyVault/vaults@2025-05-01' existing = {
  name: keyVaultName
}

resource googleClientId 'Microsoft.KeyVault/vaults/secrets@2025-05-01' existing = {
  parent: kv
  name: 'google-client-id'
}

resource googleClientSecret 'Microsoft.KeyVault/vaults/secrets@2025-05-01' existing = {
  parent: kv
  name: 'google-client-secret'
}

resource facebookClientId 'Microsoft.KeyVault/vaults/secrets@2025-05-01' existing = {
  parent: kv
  name: 'facebook-client-id'
}

resource facebookClientSecret 'Microsoft.KeyVault/vaults/secrets@2025-05-01' existing = {
  parent: kv
  name: 'facebook-client-secret'
}

resource discordClientId 'Microsoft.KeyVault/vaults/secrets@2025-05-01' existing = {
  parent: kv
  name: 'discord-client-id'
}

resource discordClientSecret 'Microsoft.KeyVault/vaults/secrets@2025-05-01' existing = {
  parent: kv
  name: 'discord-client-secret'
}

resource bggApiToken 'Microsoft.KeyVault/vaults/secrets@2025-05-01' existing = {
  parent: kv
  name: 'bgg-api-token'
}

// Versionless secretUri so the Container App picks up rotated secrets on restart
output secretRefs array = [
  { name: 'google-client-id',       keyVaultUrl: googleClientId.properties.secretUri,       identity: 'system' }
  { name: 'google-client-secret',   keyVaultUrl: googleClientSecret.properties.secretUri,   identity: 'system' }
  { name: 'facebook-client-id',     keyVaultUrl: facebookClientId.properties.secretUri,     identity: 'system' }
  { name: 'facebook-client-secret', keyVaultUrl: facebookClientSecret.properties.secretUri, identity: 'system' }
  { name: 'discord-client-id',      keyVaultUrl: discordClientId.properties.secretUri,      identity: 'system' }
  { name: 'discord-client-secret',  keyVaultUrl: discordClientSecret.properties.secretUri,  identity: 'system' }
  { name: 'bgg-api-token',          keyVaultUrl: bggApiToken.properties.secretUri,          identity: 'system' }
]
