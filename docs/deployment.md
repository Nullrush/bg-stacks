# BgStacks.Web — First Deployment Checklist

Everything needed to go from a fresh Azure subscription to a running Container App.
Wildcard domain setup is a follow-up after the initial deployment is healthy.

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

- [ ] Azure subscription with Container Apps, Cosmos DB, and Storage available
- [ ] Azure CLI installed and up to date (`az upgrade`)
- [ ] GitHub repo with Actions enabled (`nullrush/bg-stacks`)

---

## Step 2 — Create the resource group

- [ ] Log in and create the resource group:

```powershell
az login
az group create --name bgstacks-rg --location eastus
```

---

## Step 3 — OIDC federated identity (GitHub → Azure)

Both workflows authenticate via OIDC — no stored credentials.

- [ ] Create the Entra app registration and service principal:

```powershell
$app = az ad app create --display-name "bgstacks-github-actions" | ConvertFrom-Json
$appId = $app.appId
az ad sp create --id $appId
$spObjectId = (az ad sp show --id $appId | ConvertFrom-Json).id
```

- [ ] Grant Contributor on the resource group:

```powershell
$subId = (az account show | ConvertFrom-Json).id
az role assignment create `
  --assignee $spObjectId `
  --role Contributor `
  --scope "/subscriptions/$subId/resourceGroups/bgstacks-rg"
```

- [ ] Add the federated credential for push to `main`:

```powershell
az ad app federated-credential create --id $appId --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:nullrush/bg-stacks:ref:refs/heads/main",
  "audiences": ["api://AzureADMyOrg"]
}'
```

- [ ] Note down the values you'll need for GitHub vars:

```powershell
Write-Host "AZURE_CLIENT_ID:       $appId"
Write-Host "AZURE_TENANT_ID:       $((az account show | ConvertFrom-Json).tenantId)"
Write-Host "AZURE_SUBSCRIPTION_ID: $subId"
Write-Host "SP object ID (for Key Vault later): $spObjectId"
```

---

## Step 4 — Register OAuth apps

You need a client ID and secret from each provider before running the Bicep deployment.

You won't know the real redirect URI until after Step 6. Use
`https://placeholder.example.com/callback` for now — you'll update it in Step 10.

The final URI pattern is: `https://<containerAppFqdn>/.auth/login/<provider>/callback`

### Google

- [ ] Go to [console.cloud.google.com](https://console.cloud.google.com/)
- [ ] Create a project (top-left picker → **New Project**) or select an existing one
- [ ] **APIs & Services → OAuth consent screen**
  - User type: **External**
  - Fill in App name (`BgStacks`), support email, developer contact email
  - Scopes: confirm `openid`, `email`, `profile` are listed (they're defaults)
  - Test users: add your own email while in testing mode
  - Save and continue through all screens
- [ ] **APIs & Services → Credentials → + Create Credentials → OAuth 2.0 Client ID**
  - Application type: **Web application**
  - Name: `BgStacks Web`
  - Authorized redirect URIs: `https://placeholder.example.com/callback`
  - Click **Create**
- [ ] Copy the **Client ID** and **Client secret** from the confirmation dialog
- [ ] Save: `GOOGLE_CLIENT_ID` = Client ID, `GOOGLE_CLIENT_SECRET` = Client secret

> The app starts in Testing mode — only listed test users can sign in. To allow any
> Google account, go to **OAuth consent screen → Publish App** when ready.

---

### Facebook

- [ ] Go to [developers.facebook.com](https://developers.facebook.com/) and log in
- [ ] **My Apps → Create App**
  - Use case: **Authenticate and request data from users** (or **Other → Consumer**)
  - App name: `BgStacks`, contact email: your email
- [ ] **App settings → Basic** — copy the **App ID** and **App secret** (click Show)
- [ ] Add Facebook Login: left sidebar → **Add product → Facebook Login → Set up (Web)**
  - Site URL: `https://bgstacks.com`
- [ ] **Facebook Login → Settings**
  - Valid OAuth Redirect URIs: `https://placeholder.example.com/callback`
  - Save changes
- [ ] Save: `FACEBOOK_CLIENT_ID` = App ID, `FACEBOOK_CLIENT_SECRET` = App secret

> Facebook restricts sign-in to app admins and testers until the app passes App Review.
> For a private event tool, development mode is likely fine — add attendees as testers
> at **Roles → Testers**.

---

### Discord

- [ ] Go to [discord.com/developers/applications](https://discord.com/developers/applications) and log in
- [ ] **New Application** → name it `BgStacks` → Create
- [ ] Left sidebar → **OAuth2**
  - Copy the **Client ID**
  - Click **Reset Secret** → confirm → copy the **Client Secret** (shown once — save it now)
- [ ] Under **Redirects**, add `https://placeholder.example.com/callback` → Save changes
- [ ] Save: `DISCORD_CLIENT_ID` = Client ID, `DISCORD_CLIENT_SECRET` = Client secret

---

## Step 5 — GitHub repository configuration

Go to **Settings → Secrets and variables → Actions** in the GitHub repo.

### Variables (not secret)

- [ ] Add variable `AZURE_CLIENT_ID` — app registration client ID from Step 3
- [ ] Add variable `AZURE_TENANT_ID` — Entra tenant ID from Step 3
- [ ] Add variable `AZURE_SUBSCRIPTION_ID` — subscription ID from Step 3
- [ ] Add variable `AZURE_RESOURCE_GROUP` — `bgstacks-rg`

### Secrets

- [ ] Add secret `GOOGLE_CLIENT_ID`
- [ ] Add secret `GOOGLE_CLIENT_SECRET`
- [ ] Add secret `FACEBOOK_CLIENT_ID`
- [ ] Add secret `FACEBOOK_CLIENT_SECRET`
- [ ] Add secret `DISCORD_CLIENT_ID`
- [ ] Add secret `DISCORD_CLIENT_SECRET`

---

## Step 6 — Initial Bicep deployment (no wildcard cert yet)

The app will be reachable at the default ACA FQDN. Wildcard cert is Step 12.

- [ ] Run the deployment:

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

- [ ] Note the outputs:

```powershell
$outputs = az deployment group show `
  --resource-group bgstacks-rg `
  --name main `
  --query properties.outputs `
  | ConvertFrom-Json

Write-Host "App URL:         https://$($outputs.containerAppFqdn.value)"
Write-Host "ACA static IP:   $($outputs.environmentStaticIp.value)"   # needed for DNS in Step 12
Write-Host "Cosmos endpoint: $($outputs.cosmosEndpoint.value)"
Write-Host "Blob endpoint:   $($outputs.blobEndpoint.value)"
```

---

## Step 7 — Push the first Docker image

- [ ] Trigger `app.yml` with an empty commit:

```powershell
git commit --allow-empty -m "chore: trigger first app.yml deploy"
git push
```

- [ ] Watch the **Actions** tab — the workflow runs `dotnet test`, builds and pushes
  `ghcr.io/nullrush/bg-stacks:<sha>` to GHCR, then calls `az containerapp update`
- [ ] Confirm the workflow completes green

The Container App scales to zero when idle — the first request after a cold start may
take a few seconds.

---

## Step 8 — Seed Blob Storage (event game data)

The blob path pattern is `events/{slug}/games.json` (and `mechanics.json`, `categories.json`).

- [ ] Upload game data for the first event (swap `gw-2026-pnw` for your slug):

```powershell
$storageAccount = "bgstacksprodblob"
$slug = "gw-2026-pnw"

az storage blob upload `
  --account-name $storageAccount `
  --container-name events `
  --name "$slug/games.json" `
  --file public/games.json `
  --auth-mode login

az storage blob upload `
  --account-name $storageAccount `
  --container-name events `
  --name "$slug/mechanics.json" `
  --file public/mechanics.json `
  --auth-mode login

az storage blob upload `
  --account-name $storageAccount `
  --container-name events `
  --name "$slug/categories.json" `
  --file public/categories.json `
  --auth-mode login
```

> `--auth-mode login` uses your `az login` session. The storage account has no public
> access — only the Container App's managed identity (Blob Data Reader) and your CLI can read blobs.

---

## Step 9 — Seed Cosmos DB (event record)

- [ ] Create the event document (the `id` and `slug` fields must match):

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

- [ ] Repeat for any additional events. Use `"isPublic": false` to hide an event from
  `/api/events` until it's ready.

---

## Step 10 — Update OAuth redirect URIs

Now that you know the real FQDN from Step 6, replace the placeholder URIs you set in Step 4.

- [ ] **Google** — APIs & Services → Credentials → BgStacks Web → Authorized redirect URIs:
  replace placeholder with `https://<containerAppFqdn>/.auth/login/google/callback`
- [ ] **Facebook** — Facebook Login → Settings → Valid OAuth Redirect URIs:
  replace placeholder with `https://<containerAppFqdn>/.auth/login/facebook/callback`
- [ ] **Discord** — OAuth2 → Redirects:
  replace placeholder with `https://<containerAppFqdn>/.auth/login/discord/callback`

> Once the wildcard domain is live (Step 12), update these again to use
> `https://bgstacks.com/.auth/login/<provider>/callback`.

---

## Step 11 — Smoke test

- [ ] Verify the live endpoints:

```powershell
$fqdn = "<containerAppFqdn from Step 6>"

# Should return { clientPrincipal: null }
Invoke-RestMethod "https://$fqdn/.auth/me"

# Should return the gw-2026-pnw event
Invoke-RestMethod "https://$fqdn/api/events"

# Should return 404 — no event slug on the default FQDN
try { Invoke-RestMethod "https://$fqdn/games.json" } catch { $_.Exception.Response.StatusCode }

# Should return HTML
Invoke-RestMethod "https://$fqdn/"
```

- [ ] Sign in via each OAuth provider and confirm `/.auth/me` returns a `clientPrincipal`
  with a 64-char `userId`

---

## Step 12 — Wildcard domain (follow-up)

Do this once you have a wildcard TLS certificate (PFX) for `*.bgstacks.com`.

- [ ] Re-run Bicep with `kvCertReaderObjectId` to provision Key Vault:

```powershell
az deployment group create `
  --resource-group bgstacks-rg `
  --template-file infra/main.bicep `
  --parameters infra/parameters/main.bicepparam `
  --parameters `
    kvCertReaderObjectId="<spObjectId from Step 3>" `
    googleClientId="<...>" `
    googleClientSecret="<...>" `
    facebookClientId="<...>" `
    facebookClientSecret="<...>" `
    discordClientId="<...>" `
    discordClientSecret="<...>"
```

- [ ] Import the wildcard cert to Key Vault:

```powershell
az keyvault certificate import `
  --vault-name bgstacks-prod-kv `
  --name wildcard-bgstacks-com `
  --file wildcard.pfx
```

- [ ] Get the cert resource ID:

```powershell
$certId = (az keyvault certificate show `
  --vault-name bgstacks-prod-kv `
  --name wildcard-bgstacks-com `
  | ConvertFrom-Json).id
Write-Host "wildcardCertKeyVaultId: $certId"
```

- [ ] Update `infra/parameters/main.bicepparam`:

```
param wildcardCertKeyVaultId = '<certId from above>'
param kvCertReaderObjectId   = '<spObjectId from Step 3>'
```

- [ ] Add DNS records to your `bgstacks.com` zone:

| Type | Name | Value |
|---|---|---|
| `A` | `*` | `environmentStaticIp` from Bicep outputs (Step 6) |
| `TXT` | `asuid` | Verification token from ACA environment → Custom domain → Add |

- [ ] Commit the updated `main.bicepparam` and push — `infra.yml` will redeploy
- [ ] Update OAuth redirect URIs at all three providers to use `bgstacks.com`
- [ ] Verify a subdomain:

```powershell
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
