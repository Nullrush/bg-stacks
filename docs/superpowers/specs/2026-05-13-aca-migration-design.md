# ACA Migration Design Spec
*Date: 2026-05-13*

## Problem

Azure Static Web Apps requires the Standard plan ($9/month) for custom OAuth providers (Google, Apple). The Free plan silently falls back to Entra ID. Additionally, the platform vision requires wildcard subdomain support (e.g. `gw-2026-pnw.bgstacks.com`) to host arbitrary numbers of simultaneous events, which SWA's 5-domain cap does not accommodate.

## Solution

Migrate from SWA to a single ASP.NET Core Container App on Azure Container Apps (consumption plan). The app:
- Serves `public/` static files via `UseStaticFiles()`
- Re-implements the `/.auth/*` URL contract using ASP.NET Core OAuth cookie middleware
- Re-implements `/api/tags` in C#, backed by Cosmos DB NoSQL
- Routes event game data by `Host` header subdomain, fetching per-event `games.json` from Azure Blob Storage
- Supports wildcard custom domains via ACA environment DNS suffix

Infrastructure is defined as Bicep. Deployment is via GitHub Actions pushing to GHCR.

---

## Architecture

```
Browser
  ├─ /.auth/login/{provider}?post_login_redirect_uri=/   ← ASP.NET Core OAuth middleware
  ├─ /.auth/logout?post_logout_redirect_uri=/            ← clears auth cookie, redirects
  ├─ /.auth/me                                           ← returns clientPrincipal from cookie
  ├─ /games.json                                         ← dynamic: resolved from Blob Storage by event slug
  ├─ /mechanics.json, /categories.json                   ← same, per-event from Blob Storage
  ├─ /api/tags (GET/PUT)                                 ← Cosmos DB NoSQL, managed identity
  └─ /*                                                  ← static files from wwwroot/

Azure Container Apps (consumption plan, 0–3 replicas)
  ├─ System-assigned managed identity
  │    ├─ Cosmos DB Built-in Data Contributor role → tags storage
  │    └─ Storage Blob Data Reader role → event game data
  ├─ Wildcard domain: *.bgstacks.com (ACA environment DNS suffix)
  └─ Secrets: OAuth client IDs + secrets (Google, Facebook, Discord)

Azure Cosmos DB (NoSQL, free tier)
  ├─ Database: bgstacks / Container: usertags
  │    PartitionKey: /userId, id: "tags", _etag: optimistic concurrency
  └─ Database: bgstacks / Container: events
       PartitionKey: /slug, id: slug
       Fields: name, eventDate, isPublic

Azure Storage Account (Blob)
  └─ Container: events
       gw-2026-pnw/games.json
       gw-2026-pnw/mechanics.json
       gw-2026-pnw/categories.json
       geekwaywest2026/...
```

---

## Project Structure

```
bg-stacks/
  src/
    BgStacks.Web/
      Domain/
        Tags/
          UserTags.cs               — aggregate root: UserId + Tags + eTag
          UserId.cs                 — value object: SHA256(provider:sub), 64-char hex
          Tags.cs                   — value object: { want: int[], played: int[] }
          ITagsRepository.cs        — GetAsync(UserId) / SaveAsync(UserTags, string? etag)
        Events/
          EventSlug.cs              — value object: validated lowercase slug (letters, digits, hyphens)
          Event.cs                  — aggregate root: slug, name, eventDate, isPublic
          EventData.cs              — entity: games, mechanics, categories for one event
          IEventRepository.cs       — GetAsync(EventSlug) / ListPublicAsync() / SaveAsync(Event)
          IEventDataRepository.cs   — GetAsync(EventSlug)
      Application/
        Tags/
          TagsService.cs            — GetTags, SaveTags (eTag/conflict logic)
        Events/
          EventService.cs           — ListPublicEvents, GetEvent (registry queries)
          EventDataService.cs       — GetGameData (blob fetch + caching policy)
      Infrastructure/
        Tags/
          CosmosTagsRepository.cs   — ITagsRepository via Microsoft.Azure.Cosmos SDK
        Events/
          CosmosEventRepository.cs  — IEventRepository via Microsoft.Azure.Cosmos SDK
          BlobEventDataRepository.cs — IEventDataRepository via Azure.Storage.Blobs SDK
        Auth/
          OAuthUserIdFactory.cs     — derives UserId + userDetails + picture from ClaimsPrincipal
      Presentation/
        Auth/
          AuthEndpoints.cs          — MapGroup("/.auth"): login/{provider}, logout, me
        Api/
          TagsEndpoints.cs          — MapGroup("/api"): GET /tags, PUT /tags
          EventsEndpoints.cs        — GET /api/events (public event list for home page)
        Events/
          EventMiddleware.cs        — resolves EventSlug from Host header → HttpContext.Items; root domain → null slug (home page context)
          GamesJsonEndpoint.cs      — GET /games.json, /mechanics.json, /categories.json
      wwwroot/
        home.html                   — root domain home page: lists public events, upcoming vs. archive
        home.js                     — fetches GET /api/events, renders event cards
        index.html                  — event subdomain page (existing, unchanged)
        app.js, styles.css, icons, sw.js, manifest.json
      Program.cs
      Dockerfile
      appsettings.json
      appsettings.Development.json
  infra/
    main.bicep
    modules/
      environment.bicep             — ACA managed environment + wildcard DNS suffix
      containerapp.bicep            — Container App: ingress, scaling, managed identity
      cosmos.bicep                  — Cosmos DB account, database, container
      storage.bicep                 — Storage Account + events blob container
    parameters/
      main.bicepparam               — production
      main.dev.bicepparam           — dev/staging
  api/                              — existing Node.js functions (reference only, to be removed)
  public/                           — existing static files (source for wwwroot/)
  scripts/                          — data pipeline (unchanged)
  docs/
  .github/workflows/
    infra.yml                       — deploys Bicep on infra/** changes
    app.yml                         — builds + pushes image + updates container on src/** changes
```

**DDD layer rules:**
- `Domain/` — zero dependencies outside itself (no Azure SDK, no ASP.NET)
- `Application/` — depends only on domain interfaces; owns use-case and caching policy logic
- `Infrastructure/` — depends on domain interfaces + Azure SDKs; registered in DI via `Program.cs`
- `Presentation/` — depends on application services; endpoints are thin (validate → call service → return)

Future A→B split: `Domain/` + `Application/` → `BgStacks.Core`, `Infrastructure/` → `BgStacks.Infrastructure`, remainder stays in `BgStacks.Web`.

---

## Auth System

### Session mechanism

ASP.NET Core cookie authentication. After OAuth callback, the framework issues an encrypted, signed auth cookie. No server-side session store. `/.auth/me` reads `HttpContext.User` from the cookie and returns the `clientPrincipal` shape.

### Providers (v1)

| Provider | Package | Notes |
|---|---|---|
| Google | `Microsoft.AspNetCore.Authentication.Google` | Built-in |
| Facebook | `Microsoft.AspNetCore.Authentication.Facebook` | Built-in |
| Discord | `AspNet.Security.OAuth.Discord` | Community package |

Apple is deferred to v2 (requires JWT client secret with 6-month rotation via Apple Developer private key).

### `/.auth/*` URL contract (unchanged from SWA)

```
GET /.auth/login/google?post_login_redirect_uri=/    → redirects to Google OAuth
GET /.auth/login/facebook?post_login_redirect_uri=/  → redirects to Facebook OAuth
GET /.auth/login/discord?post_login_redirect_uri=/   → redirects to Discord OAuth
GET /.auth/logout?post_logout_redirect_uri=/         → clears cookie, redirects
GET /.auth/me                                        → { clientPrincipal: {...} | null }
```

OAuth callback URLs registered with each provider: `/.auth/login/{provider}/callback`.

### `userId` derivation

```
userId = lowercase hex of SHA256("{provider}:{sub}")
```

- `provider` = `"google"` | `"facebook"` | `"discord"` (lowercase)
- `sub` = provider's stable user identifier
- Result: 64-char hex string — stable across logins, provider-namespaced, no PII

### `/.auth/me` response shape

```json
{
  "clientPrincipal": {
    "userId": "a3f9...c12e",
    "userDetails": "user@example.com",
    "identityProvider": "google",
    "claims": [
      { "typ": "picture", "val": "https://lh3.googleusercontent.com/..." }
    ]
  }
}
```

When unauthenticated: `{ "clientPrincipal": null }`.

`userDetails` = email if available (Google, Facebook), otherwise display name (Discord `username`).

`claims` picture sources by provider:
- Google: `picture` claim from ID token
- Facebook: constructed as `https://graph.facebook.com/{id}/picture?type=square`
- Discord: constructed as `https://cdn.discordapp.com/avatars/{id}/{avatar}.png`

The `claims` array is extensible — a future `/api/me` PUT endpoint can store a user-supplied avatar URL in Cosmos DB and `/.auth/me` prefers it over the provider's picture with no structural changes.

---

## Event Routing + Game Data

### Host → event slug resolution (`EventMiddleware`)

Runs early in the pipeline. Reads the `Host` header, strips the configured base domain (`bgstacks.com`), treats the leftmost label as the event slug. If the hostname matches the bare base domain (no subdomain), the slug is null — signalling home page context.

```
gw-2026-pnw.bgstacks.com  →  EventSlug("gw-2026-pnw")   (event page)
bgstacks.com               →  null                         (home page)
localhost:5000             →  EventSlug("dev")             (configurable dev fallback)
```

The resolved `EventSlug` (or null) is stored in `HttpContext.Items`. Endpoints that require an event slug (game data, tags API) return 404 when null. The home page and `/api/events` are reachable from both contexts.

### Blob Storage layout

```
container: events
  {slug}/games.json
  {slug}/mechanics.json
  {slug}/categories.json
```

`BlobEventDataRepository` fetches all three files for the resolved slug and returns an `EventData` entity. `EventDataService` wraps this in `IMemoryCache` (key = event slug, default TTL = 1 hour). Cache is invalidated by app restart or a future admin endpoint.

### Dynamic game data endpoints

`/games.json`, `/mechanics.json`, `/categories.json` are Minimal API routes — not static files. They resolve the slug from `HttpContext.Items`, call `EventDataService`, and return the cached blob as `application/json`. The frontend's `fetch('/games.json')` is unchanged.

### Event registry (Cosmos DB)

Event metadata is stored in a second Cosmos DB container, separate from tags:

```
Database:      bgstacks
Container:     events
PartitionKey:  /slug
id:            same as slug

Fields:
  slug        string   "gw-2026-pnw"
  name        string   "Geekway 2026 Prime Play & Win"
  eventDate   string   ISO 8601 date "2026-06-12" (start date of convention)
  isPublic    bool     controls home page visibility
```

`GET /api/events` returns all documents where `isPublic = true`, ordered by `eventDate` descending. The home page (`home.js`) consumes this to render two sections: upcoming/active events (eventDate ≥ today) and past/archive events (eventDate < today). Private events (isPublic = false) are never returned by this endpoint — they are accessible only via direct subdomain URL.

### Home page (`home.html` + `home.js`)

Served at the root domain (`bgstacks.com`). Calls `GET /api/events` on load and renders:
- **Upcoming / Active** — events where `eventDate ≥ today`, sorted soonest first
- **Past / Archive** — events where `eventDate < today`, sorted most recent first

Each event card links to its subdomain URL. Auth state (sign-in button) is present on the home page using the same `/.auth/me` check as the event page. The home page shares `styles.css` and `sw.js` with the event page for consistent platform identity.

`manifest.json` uses generic platform-level identity ("BG Stacks") so it works for both the root domain and event subdomains as a PWA.

### Adding a new event

1. Run pipeline scripts locally → produces `games.json`, `mechanics.json`, `categories.json`
2. Upload to `events/{slug}/` in Blob Storage
3. Register the event in the Cosmos DB `events` container (name, slug, eventDate, isPublic)
4. Add a CNAME DNS record for the new subdomain pointing at the ACA environment IP
5. Done — no redeploy, no Bicep changes

---

## Tags API

### HTTP contract (unchanged from SWA)

```
GET  /api/tags
  → 200 { tags: { want: [ids], played: [ids] }, etag: "W/\"...\"" }
  → 204  (no record exists yet)

PUT  /api/tags
  body: { tags: { want: [ids], played: [ids] }, etag: string | null }
  → 200 { etag: "W/\"...\"" }
  → 409  (stale eTag — client re-fetches and retries)
```

Both endpoints require authentication (`RequireAuthorization()`). `UserId` is read from `HttpContext.User` via `OAuthUserIdFactory` — no header parsing.

### Cosmos DB schema

```
Account:       bgstacks (free tier)
Database:      bgstacks
Container:     usertags
PartitionKey:  /userId
id:            "tags"
Fields:        userId, want (int[]), played (int[])
_etag:         managed by Cosmos DB, used for optimistic concurrency
```

`CosmosTagsRepository` uses `Microsoft.Azure.Cosmos` SDK with `DefaultAzureCredential`. Optimistic concurrency: `etag: null` → `CreateItemAsync`; etag present → `ReplaceItemAsync` with `ItemRequestOptions { IfMatchEtag = clientEtag }`. Cosmos DB 412 → `ConflictException` → 409 to client.

### Domain model

```csharp
// ITagsRepository
Task<(Tags? tags, string? etag)> GetAsync(UserId userId);
Task<string> SaveAsync(UserTags userTags, string? clientEtag); // throws ConflictException on stale eTag
```

`TagsService` (application layer) owns the use-case logic; `TagsEndpoints` (presentation) calls it and maps results to HTTP responses.

---

## Infrastructure (Bicep)

### Resources

| Module | Resource | Notes |
|---|---|---|
| `environment.bicep` | `Microsoft.App/managedEnvironments` | Wildcard DNS suffix `*.bgstacks.com`, wildcard TLS cert |
| `containerapp.bicep` | `Microsoft.App/containerApps` | External ingress port 8080, min 0 / max 3 replicas, system-assigned identity |
| `cosmos.bicep` | `Microsoft.DocumentDB/databaseAccounts` | Free tier, NoSQL API, database `bgstacks`, containers `usertags` + `events` |
| `storage.bicep` | `Microsoft.Storage/storageAccounts` | Blob Storage only, container `events` |
| `keyvault.bicep` | `Microsoft.KeyVault/vaults` | Stores wildcard TLS certificate for ACA environment DNS suffix |

### Managed identity RBAC

Container App system-assigned identity is granted:
- `Cosmos DB Built-in Data Contributor` on the Cosmos DB account
- `Storage Blob Data Reader` on the Storage Account

C# uses `DefaultAzureCredential` for all storage access. No connection strings stored anywhere for storage.

OAuth client secrets (Google, Facebook, Discord) are stored as Container Apps secrets and injected as environment variables.

### Wildcard domain

ACA environment configured with `*.bgstacks.com` as custom DNS suffix. Requires a wildcard TLS certificate (stored in Key Vault, referenced from `environment.bicep`). Let's Encrypt can issue this via DNS-01 challenge; renewal can be automated via a scheduled workflow.

Individual event subdomains require only a CNAME DNS record — no Bicep changes.

### GitHub Actions workflows

**`infra.yml`** — triggers on `infra/**` changes:
```
az login (OIDC) → az deployment group create --template-file infra/main.bicep
```

**`app.yml`** — triggers on `src/**` changes:
```
dotnet test → docker build → push ghcr.io/nullrush/bg-stacks:{sha} → az containerapp update --image ...
```

Uses existing OIDC login pattern (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`). Image tagged with commit SHA for rollback.

---

## Local Development

`appsettings.Development.json` configures:
- Azurite for Blob Storage emulation (event game data)
- Cosmos DB emulator for tags
- `EventSlug` fallback: `"dev"` (reads from a local path or Azurite)
- OAuth redirect URIs pointing to `localhost`

`dotnet run` serves everything on `http://localhost:5000`. No SWA CLI needed.

---

## What Is Not Changing

- `public/` static files — copied to `wwwroot/` unchanged (minus `games.json`)
- Frontend `/.auth/*` URL contract — preserved exactly
- Frontend `/api/tags` contract — preserved exactly
- Tags data format `{ want: [ids], played: [ids] }` — preserved
- eTag optimistic concurrency flow — preserved
- Data pipeline scripts (`parse-bgg-csv.mjs`, `enrich-from-bgg.js`) — unchanged
- `localStorage` fallback for unauthenticated users — unchanged

---

## Out of Scope (this migration)

- Event admin UI (events registered directly in Cosmos DB for now)

- Apple Sign In (v2 — requires JWT client secret with private key rotation)
- Per-event tag scoping (issue #11)
- User-supplied avatar override (`/api/me` PUT endpoint)
- Share link feature
- Admin cache invalidation endpoint
- Real-time multi-device sync
