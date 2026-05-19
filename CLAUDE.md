# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Knowledge vault

`vault/` is a gitignored Obsidian vault for local notes, plans, and deployment docs.
Write all superpowers plan files, specs, and scratchpad documents to `vault/superpowers/` (plans → `vault/superpowers/plans/`, specs → `vault/superpowers/specs/`).
Do not write planning or documentation files anywhere else in the repo.

# Geekway 2026 Prime PnW — sortable game list

Static site that displays the BoardGameGeek geeklist for Geekway 2026 Prime's Play & Win shelves as a sortable, filterable table. Deploys to Azure Static Web Apps.

## Architecture

No build step. `public/` is the deploy artifact.
`public/index.html` is markup only; `public/app.js` does the fetch / render / sort / filter / state-persistence work; `public/styles.css` carries every visual rule. Everything is vanilla HTML/CSS/JS — no frameworks, no bundler.

The `api/` directory is an Azure Functions app (Node.js, CommonJS) that provides a `/api/tags` endpoint for cloud sync of wishlist/played tags. It talks to Azure Table Storage (`usertags` table). Auth is handled by Azure Static Web Apps' built-in identity providers (Google, Apple) via `/.auth/*` routes — the function reads the `x-ms-client-principal` header to identify the user.

## Commands

```bash
npm install                     # one time, installs serve + swa-cli
npm run dev                     # SWA CLI: serves public/ + api/ on http://localhost:4280
npm run dev:thin                # serve public/ only on http://localhost:3000 (no API/auth)
npm run refresh -- <csv-path>   # step 1: rebuild games.json from a BGG CSV export
node scripts/enrich-from-bgg.js # step 2: add mechanics/categories/thumbnails/ranks to games.json
npm run deploy                  # one-shot SWA CLI deploy (sources .env.local)

# API tests (run from repo root, not api/)
cd api && npm test
```

## Data pipeline

`games.json` is produced in two steps:

1. **`npm run refresh -- <csv-path>`** (`parse-bgg-csv.mjs`) — converts a BGG geeklist CSV export into `public/games.json` with player counts, play times, ratings, and poll data. Required CSV columns: `id`, `name`, `minplayers`, `maxplayers`, `playingtime`, `minplaytime`, `maxplaytime`, `average`, `bayesaverage`, `averageweight`, `usersrated`, and `1player` through `20player` (poll values: `B`/`R`/`N`). The CSV comes from a BGG geeklist export (e.g. via BGG1Tool).

2. **`node scripts/enrich-from-bgg.js`** — fetches enriched data (mechanics, categories, thumbnails, descriptions, BGG rank, sub-category ranks) from the BGG API for each game and merges it into `games.json`. Results are cached in `scripts/.bgg-cache/` so the script is safe to interrupt and resume. Requests are throttled with a random 30–120 second delay to avoid taxing BGG. Pass `--merge-only` to skip fetching and just merge cached data.

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

## API backend (`api/`)

- `api/tags.js` — Azure Functions entry point; routes GET/PUT `/api/tags`
- `api/lib/handlers.js` — storage logic using `@azure/data-tables` (Azure Table Storage, table `usertags`, partition key = userId, row key = `'tags'`). Uses optimistic concurrency (eTag); returns `409` on conflict.
- `api/lib/tagsFormat.js` — pure functions for converting between cloud format `{ want: [ids], played: [ids] }` and runtime format `{ [gameId]: { want?: true, played?: true } }`; also `mergeTags` and `tagsAreEqual`.
- `api/lib/crypto-polyfill.js` — required before anything that needs `crypto` in the Azure Functions runtime.
- `api/local.settings.json` — local dev config (gitignored); needs `AZURE_STORAGE_CONNECTION_STRING`.

Cloud format stores game IDs as numbers. Runtime format uses string keys (object property names) but the IDs are numeric values. This asymmetry exists because `localStorage` JSON keys are always strings.

## Conventions

- Keep dependencies near zero. The whole point is a single-page site that loads instantly.
- Don't introduce a bundler or framework without a strong reason.
- `SORT_KEYS` map keys in `app.js` are intentionally short (e.g. `pmn`, `tmn`, `ob`) because they appear in the URL (`?sort=...`). The *values* they map to use the verbose JSON field names. Changing a key breaks shared/bookmarked URLs.
- `.env.local` is gitignored. The deployment token lives there for manual `npm run deploy`. Never commit it.

## Deploy

Two paths, both work:

- **GitHub Actions (CI)** — workflow lives at `.github/workflows/azure-static-web-apps.yml`. Set `AZURE_STATIC_WEB_APPS_API_TOKEN` as a repo secret in GitHub. Pushing to `main` triggers a deploy.
- **Manual SWA CLI** — put the deployment token in `.env.local` (copy `.env.local.example` first), then `npm run deploy`. Useful for ad-hoc pushes without going through CI.

## C# Application (`src/BgStacks.Web/`)

ASP.NET Core Minimal API. DDD structure: Domain → Application → Infrastructure → Presentation.

```bash
dotnet run --project src/BgStacks.Web          # requires Cosmos emulator + Azurite running locally
dotnet test tests/BgStacks.Web.Tests
docker build -f src/BgStacks.Web/Dockerfile -t bgstacks:local .
```

### Local dev prerequisites
- [Cosmos DB emulator](https://aka.ms/cosmosdb-emulator) running on https://localhost:8081/
- [Azurite](https://github.com/Azure/Azurite) running: `npx azurite --silent`
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
