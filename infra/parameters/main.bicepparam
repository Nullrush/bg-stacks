using '../main.bicep'

param env = 'prod'
param image = 'ghcr.io/nullrush/bg-stacks:latest'

param googleClientId = ''
param googleClientSecret = ''
param facebookClientId = ''
param facebookClientSecret = ''
param discordClientId = ''
param discordClientSecret = ''

param wildcardCertKeyVaultId = ''
param kvCertReaderObjectId = ''
