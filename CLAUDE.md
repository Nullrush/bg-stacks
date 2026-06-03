# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Knowledge vault

`vault/` is a gitignored Obsidian vault for local notes, plans, and deployment docs.
Write all superpowers plan files, specs, and scratchpad documents to `vault/superpowers/` (plans → `vault/superpowers/plans/`, specs → `vault/superpowers/specs/`).
Do not write planning or documentation files anywhere else in the repo.

# Geekway 2026 Prime PnW — sortable game list

Sortable, filterable index of the Geekway 2026 Prime Play & Win geeklist. Served by the C# Container App — there is no separate static site deployment.

## Architecture

No build step. `src/BgStacks.Web/wwwroot/` is the frontend deploy artifact.
`wwwroot/index.html` is markup only; `wwwroot/app.js` does the fetch / render / sort / filter / state-persistence work; `wwwroot/styles.css` carries every visual rule. Everything is vanilla HTML/CSS/JS — no frameworks, no bundler.

Game data is fetched on demand from the BGG API by the C# backend and cached in Azure Blob Storage (FusionCache L2) and Cosmos DB. There is no checked-in `games.json`.

## State the UI persists

- Filters + sort state → URL query params and `localStorage['gpw.state']`. URL wins on load; localStorage is the fallback. Anything matching `DEFAULTS` is omitted from the URL.
- Per-game `wishlist` / `played` tags → `localStorage['gpw.tags']` keyed by game id. When signed in, tags also sync to Azure Table Storage via `/api/tags` (optimistic concurrency via eTag; conflict modal on divergence).
- Theme override (light / dark / system) → `localStorage['gpw.theme']`.
- Desktop-mode override on phones → `localStorage['gpw.view']` (swaps the viewport `<meta>` to `width=1280`).

## Data shape

Each entry in `games.json` uses verbose field names so the file is self-describing for outside consumers:

```json
{
  "id": 418059,
  "name": "SETI: Search for Extraterrestrial Intelligence",
  "players": "1-4",
  "minPlayers": 1,
  "maxPlayers": 4,
  "bestPlayers": [3],
  "recommendedPlayers": [3],
  "weight": 3.83,
  "time": "40-160",
  "minTime": 40,
  "maxTime": 160,
  "avgRating": 8.42,
  "geekRating": 8.03,
  "votes": 19245,
  "bggRank": 12,
  "subRanks": { "strategy": 5, "thematic": null, ... },
  "mechanics": ["Worker Placement"],
  "categories": ["Science Fiction"],
  "thumbnail": "https://..."
}
```

`players` and `time` are display strings (e.g. `"1-4"`, `"40-160"`); the `min*`/`max*` numeric fields are what the UI sorts by. `bestPlayers` and `recommendedPlayers` come from the BGG community poll, treated hierarchically: a player count where the winning vote was "Best" goes into both arrays; a count where the winning vote was "Recommended" goes only into `recommendedPlayers`. So **`bestPlayers` is always a subset of `recommendedPlayers`**. Player counts above 20 are capped to 20 (`PMAX_CAP` in the parser); poll lists with more than 6 entries render as `any` in the UI. `geekRating` is `0` when BGG has not yet issued a Bayesian rating (too few votes); the UI renders that as `—` and skips those games in the Hybrid column.

The runtime adds a derived `hybridRating = (avgRating + geekRating) / 2` after fetch (when `geekRating > 0`).

## Conventions

- Keep dependencies near zero. The whole point is a single-page site that loads instantly.
- Don't introduce a bundler or framework without a strong reason.
- `SORT_KEYS` map keys in `app.js` are intentionally short (e.g. `pmn`, `tmn`, `ob`) because they appear in the URL (`?sort=...`). The *values* they map to use the verbose JSON field names. Changing a key breaks shared/bookmarked URLs.
- **`wwwroot/` is the sole source of truth for all frontend assets.** Do not create duplicate copies elsewhere in the repo.

## C# Application (`src/BgStacks.Web/`)

ASP.NET Core Minimal API. DDD structure: Domain → Application → Infrastructure → Presentation.

```bash
dotnet run --project src/BgStacks.Web          # standalone; requires Cosmos emulator + Azurite running separately
dotnet test tests/BgStacks.Web.Tests
docker build -f src/BgStacks.Web/Dockerfile -t bgstacks:local .
```

### Local dev prerequisites
- When using `src/BgStacks.Web` standalone: [Cosmos DB emulator](https://aka.ms/cosmosdb-emulator) on https://localhost:8081/ and [Azurite](https://github.com/Azure/Azurite) (`npx azurite --silent`) must be running
- OAuth client IDs/secrets are not needed for local dev (auth flows won't work without them, but the rest of the app does)

### Key config (appsettings.Development.json)
- `Cosmos:ConnectionString` — emulator connection string (overrides DefaultAzureCredential for local dev)
- `Blob:ConnectionString` — Azurite connection string (overrides DefaultAzureCredential for local dev)
- `Events:DevFallbackSlug` — slug used when running on localhost (default: `"dev"`)

## CI/CD

| Trigger | Workflow | What it does |
|---|---|---|
| Push to `main` touching `infra/**` | `infra.yml` | Deploy Bicep (idempotent) via deployment stack |
| Push to `main` touching `src/**` | `app.yml` | Test → build image → push GHCR → update Container App |
| Manual `workflow_dispatch` | Either | Run on demand |

**Never commit `infra/**` and `src/**` changes together.** Both workflows trigger simultaneously and race to update the Container App. `infra.yml` always deploys with `image = 'ghcr.io/nullrush/bg-stacks:latest'`, which can stomp the SHA-tagged image `app.yml` just pushed — or land before the infrastructure the new app code depends on exists. Always use two commits: infra first, wait for green, then app.
