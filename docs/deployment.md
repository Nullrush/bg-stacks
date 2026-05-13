# BgStacks.Web — First Deployment Guide

Everything needed to go from a fresh Azure subscription to a running Container App.
Wildcard domain setup is a follow-up step after the initial deployment is healthy.

---

## Resource names

These are computed by Bicep with `env = 'prod'`:

| Resource | Name |
|---|---|
| Resource group | `bgstacks-rg` (your choice, passed via `AZURE_RESOURCE_GROUP`) |
| Cosmos DB account | `bgstacks-prod` |
| Storage account | `bgstacksprodblob` |
| Key Vault | `bgstacks-prod-kv` |
| ACA environment | `bgstacks-prod-env` |
| Container App | `bgstacks-prod` |

---

## Step 1 — Prerequisites

- Azure subscription with Container Apps, Cosmos DB, and Storage available
- Azure CLI installed and up to date (`az upgrade`)
- GitHub repo with Actions enabled (`nullrush/bg-stacks`)
- Docker installed locally (only needed if you want to test the image before pushing)

---

## Step 2 — Create the resource group

```powershell
az login
az group create --name bgstacks-rg --location eastus
```

---

## Step 3 — OIDC federated identity (GitHub → Azure)

Both workflows authenticate to Azure via OIDC — no stored credentials.

```powershell
# Create the Entra app registration
$app = az ad app create --display-name "bgstacks-github-actions" | ConvertFrom-Json
$appId = $app.appId

# Create a service principal for it
az ad sp create --id $appId

# Get the SP object ID (needed for role assignments)
$spObjectId = (az ad sp show --id $appId | ConvertFrom-Json).id

# Grant Contributor on the resource group (needed for Bicep deployments)
$subId = (az account show | ConvertFrom-Json).id
az role assignment create `
  --assignee $spObjectId `
  --role Contributor `
  --scope "/subscriptions/$subId/resourceGroups/bgstacks-rg"

# Add a federated credential for push to main
az ad app federated-credential create --id $appId --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:nullrush/bg-stacks:ref:refs/heads/main",
  "audiences": ["api://AzureADMyOrg"]
}'

# Print the values you need for GitHub vars
Write-Host "AZURE_CLIENT_ID:       $appId"
Write-Host "AZURE_TENANT_ID:       $((az account show | ConvertFrom-Json).tenantId)"
Write-Host "AZURE_SUBSCRIPTION_ID: $subId"
```

---

## Step 4 — GitHub repository configuration

Go to **Settings → Secrets and variables → Actions** in the GitHub repo.

### Variables (not secret)

| Name | Value |
|---|---|
| `AZURE_CLIENT_ID` | App registration client ID (from Step 3) |
| `AZURE_TENANT_ID` | Your Entra tenant ID (from Step 3) |
| `AZURE_SUBSCRIPTION_ID` | Your subscription ID (from Step 3) |
| `AZURE_RESOURCE_GROUP` | `bgstacks-rg` |

### Secrets

| Name | Where to get it |
|---|---|
| `GOOGLE_CLIENT_ID` | Google Cloud Console → APIs & Services → Credentials |
| `GOOGLE_CLIENT_SECRET` | Same |
| `FACEBOOK_CLIENT_ID` | Facebook Developers → App → App Settings → Basic |
| `FACEBOOK_CLIENT_SECRET` | Same |
| `DISCORD_CLIENT_ID` | Discord Developer Portal → Application → OAuth2 |
| `DISCORD_CLIENT_SECRET` | Same |

---

## Step 5 — Register OAuth apps

You need redirect URIs pointing at the Container App. You won't know the exact FQDN
until after the first Bicep deployment. Use a placeholder now and update after Step 7.

The FQDN pattern is: `bgstacks-prod.<aca-env-default-domain>`
(visible in the Bicep output `containerAppFqdn` after deployment)

### Google
- Console: [console.cloud.google.com](https://console.cloud.google.com/) → APIs & Services → Credentials → Create OAuth 2.0 Client ID (Web application)
- Authorized redirect URI: `https://<containerAppFqdn>/.auth/login/google/callback`

### Facebook
- Console: [developers.facebook.com](https://developers.facebook.com/) → Your App → Facebook Login → Settings
- Valid OAuth Redirect URI: `https://<containerAppFqdn>/.auth/login/facebook/callback`

### Discord
- Console: [discord.com/developers/applications](https://discord.com/developers/applications) → OAuth2 → Redirects
- Redirect: `https://<containerAppFqdn>/.auth/login/discord/callback`

> Once the wildcard domain is live, update these to `https://bgstacks.com/.auth/login/<provider>/callback`
> (the root domain — the SPA and auth live on the root, events live on subdomains).

---

## Step 6 — Initial Bicep deployment (no wildcard cert yet)

The first deployment skips the wildcard cert — the app will be reachable at the default
ACA FQDN. Add the wildcard cert as a follow-up (Step 12).

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

Note the outputs — you'll need them:

```powershell
# Grab outputs after deployment
$outputs = az deployment group show `
  --resource-group bgstacks-rg `
  --name main `
  --query properties.outputs `
  | ConvertFrom-Json

Write-Host "App URL:        https://$($outputs.containerAppFqdn.value)"
Write-Host "ACA static IP:  $($outputs.environmentStaticIp.value)"   # for DNS later
Write-Host "Cosmos endpoint: $($outputs.cosmosEndpoint.value)"
Write-Host "Blob endpoint:   $($outputs.blobEndpoint.value)"
```

---

## Step 7 — Push the first Docker image

Trigger `app.yml` by pushing a commit that touches `src/**`:

```powershell
# Trivial change to trigger the workflow
git commit --allow-empty -m "chore: trigger first app.yml deploy"
git push
```

Watch the Actions tab. The workflow:
1. Runs `dotnet test`
2. Builds and pushes the image to `ghcr.io/nullrush/bg-stacks:<sha>`
3. Calls `az containerapp update` to deploy the new image

After it completes, the Container App will have pulled the image. It scales to zero when
idle, so the first request after idle may take a few seconds to cold-start.

---

## Step 8 — Seed Blob Storage (event game data)

Upload `games.json`, `mechanics.json`, and `categories.json` for each event.
The blob path pattern is `events/{slug}/{file}`.

```powershell
$storageAccount = "bgstacksprodblob"

# Upload game data for the first event (replace gw-2026-pnw with your slug)
az storage blob upload `
  --account-name $storageAccount `
  --container-name events `
  --name "gw-2026-pnw/games.json" `
  --file public/games.json `
  --auth-mode login

az storage blob upload `
  --account-name $storageAccount `
  --container-name events `
  --name "gw-2026-pnw/mechanics.json" `
  --file public/mechanics.json `
  --auth-mode login

az storage blob upload `
  --account-name $storageAccount `
  --container-name events `
  --name "gw-2026-pnw/categories.json" `
  --file public/categories.json `
  --auth-mode login
```

> `--auth-mode login` uses your `az login` credentials. The storage account has no
> public access — only the Container App's managed identity (Blob Data Reader role)
> and your CLI session can read blobs.

---

## Step 9 — Seed Cosmos DB (event record)

The `events` container holds one document per event, keyed on `slug`.

```powershell
az cosmosdb sql document create `
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

Repeat for any additional events. Set `"isPublic": false` to hide an event from
`/api/events` until it's ready.

---

## Step 10 — Update OAuth redirect URIs

Now that you know the FQDN from Step 6, go back to each OAuth provider and fill in
the real redirect URIs.

---

## Step 11 — Smoke test

```powershell
$fqdn = "<containerAppFqdn from Step 6>"

# Auth endpoint — should return { clientPrincipal: null }
Invoke-RestMethod "https://$fqdn/.auth/me"

# Events API — should return the gw-2026-pnw event
Invoke-RestMethod "https://$fqdn/api/events"

# games.json on default FQDN — should return 404 (no event slug on root domain)
try { Invoke-RestMethod "https://$fqdn/games.json" } catch { $_.Exception.Response.StatusCode }

# Home page — should return HTML
Invoke-RestMethod "https://$fqdn/"
```

---

## Step 12 — Wildcard domain (follow-up)

Do this once you have a wildcard TLS certificate for `*.bgstacks.com`.

### 12a — Import the cert to Key Vault

```powershell
# Key Vault is only deployed when kvCertReaderObjectId is set (Step 3 SP object ID)
# Re-run Bicep with kvCertReaderObjectId to provision the vault first
az deployment group create `
  --resource-group bgstacks-rg `
  --template-file infra/main.bicep `
  --parameters infra/parameters/main.bicepparam `
  --parameters kvCertReaderObjectId="<spObjectId from Step 3>" `
  --parameters googleClientId="..." # ... same as Step 6

# Then import the cert
az keyvault certificate import `
  --vault-name bgstacks-prod-kv `
  --name wildcard-bgstacks-com `
  --file wildcard.pfx
```

### 12b — Get the cert resource ID

```powershell
$certId = (az keyvault certificate show `
  --vault-name bgstacks-prod-kv `
  --name wildcard-bgstacks-com `
  | ConvertFrom-Json).id
Write-Host "wildcardCertKeyVaultId: $certId"
```

### 12c — Update `main.bicepparam`

```
param wildcardCertKeyVaultId = '<certId from above>'
param kvCertReaderObjectId   = '<spObjectId from Step 3>'
```

### 12d — DNS records

Add these to your `bgstacks.com` zone:

| Type | Name | Value |
|---|---|---|
| `A` | `*` | `environmentStaticIp` (from Bicep output in Step 6) |
| `TXT` | `asuid` | Verification token (from ACA environment → Custom domain → Add) |

### 12e — Re-run Bicep

Commit the updated `main.bicepparam` and push to `main` — `infra.yml` will pick it up.
Or run the deployment manually as in Step 6.

### 12f — Verify a subdomain

```powershell
# Replace with a real event slug
Invoke-RestMethod "https://gw-2026-pnw.bgstacks.com/games.json"
```

---

## CI/CD summary

| Trigger | Workflow | What it does |
|---|---|---|
| Push to `main` touching `infra/**` | `infra.yml` | Deploy Bicep (idempotent) |
| Push to `main` touching `src/**` | `app.yml` | Test → build image → push GHCR → update Container App |
| Manual `workflow_dispatch` | Either | Run on demand |

The Container App always runs the SHA-tagged image from the triggering commit.
