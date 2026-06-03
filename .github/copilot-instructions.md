# Copilot Instructions — Geekway Play & Win Index

## Commands

```bash
dotnet run --project src/BgStacks.Web   # dev server (requires Cosmos emulator + Azurite)
dotnet test tests/BgStacks.Web.Tests
docker build -f src/BgStacks.Web/Dockerfile -t bgstacks:local .
```

No build step for the frontend. `src/BgStacks.Web/wwwroot/` is the sole source of truth for all frontend assets — do not create copies elsewhere.

## Architecture

No framework, no bundler. Three files own everything:

- **`wwwroot/index.html`** — static markup only; no logic
- **`wwwroot/app.js`** — all behavior: fetch, render, filter, sort, state persistence, theme, keyboard shortcuts, service worker registration
- **`wwwroot/styles.css`** — all styles; light/dark via `prefers-color-scheme` + manual `data-theme` override on `<html>`

Game data is fetched on demand from the BGG API by the C# backend (`BggGeeklistService`) and cached in Azure Blob Storage (FusionCache L2) and Cosmos DB. There is no checked-in `games.json`.

### Runtime data flow

On page load, `app.js`:
1. Runs `hydrateState()` — URL params win over `localStorage['gpw.state']`
2. Fetches `games.json`, `mechanics.json`, `categories.json` in parallel (polls on 202 while the backend loads from BGG for the first time)
3. Derives `hybridRating = (avgRating + geekRating) / 2` (only when `geekRating > 0`)
4. Calls `render()` — filters via `matches()`, sorts via `SORT_KEYS`, builds innerHTML

All state mutations go through `update(partial)`, which merges into `state`, calls `persistState()` (URL + localStorage), then `render()`.

## Key Conventions

### URL-stable sort keys
`SORT_KEYS` in `app.js` maps **short keys** (e.g. `pmn`, `tmn`, `ob`) to sort functions. These short keys appear in the URL (`?sort=pmn`). **Never rename a key** — it breaks bookmarked/shared URLs. The verbose JSON field names are used in the sort functions themselves.

### State shape
`DEFAULTS` in `app.js` is the canonical state shape. Anything matching `DEFAULTS` is omitted from the URL. Add new filter/sort state to `DEFAULTS` first, then to `serializeState` / `parseState`.

### `bestPlayers` is a subset of `recommendedPlayers`
Poll counts where the community voted "Best" land in **both** arrays. Counts voted "Recommended" land only in `recommendedPlayers`.

### Player count cap
`PMAX_CAP = 20` caps `maxPlayers` and poll arrays. Poll lists with > 6 entries render as `any` in the UI.

### `geekRating === 0` means unrated
BGG returns `0` (not `null`) when a game has too few votes for a Bayesian rating. The UI renders it as `—` and excludes those games from the Hybrid sort. Never treat `0` as a valid rating.

### localStorage keys
| Key | Contents |
|---|---|
| `gpw.state` | Serialized URL params (filters + sort) |
| `gpw.tags` | `{ [gameId]: { want?: true, played?: true } }` |
| `gpw.theme` | `"light"` \| `"dark"` (absent = system) |
| `gpw.view` | Desktop-mode override on phones |

### Sub-rank columns
`SUB_RANK_LABELS` defines the 8 BGG sub-categories (abstracts, cgs, childrens, family, party, strategy, thematic, wargames). Sort keys for these use the `sr_` prefix (e.g. `sr_strategy`). `extraRankCols` in state tracks which sub-rank columns the user has pinned; serialized as `?xrank=strategy|family`.
