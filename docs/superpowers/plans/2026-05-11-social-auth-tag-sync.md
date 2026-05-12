# Social Auth + Tag Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Google + Apple social login to the Geekway P&W Index so users can sync their "want" and "played" game tags across devices via Azure Table Storage, while preserving all existing localStorage-only behavior for logged-out users.

**Architecture:** SWA built-in auth (Google + Apple via OpenID Connect) handles the OAuth flow — no auth code needed in app.js. A single SWA Managed Function at `api/tags.js` reads/writes per-user tags in Azure Table Storage using an eTag-based version field for optimistic concurrency. The client (`app.js`) syncs on page load and after each tag change via a 1.5s debounce, with an auto-merge retry on 409 and a user-facing conflict modal on first-login divergence.

**Tech Stack:** Azure SWA built-in auth, Azure Functions v4 (Node 20, CJS), `@azure/data-tables` v13, `@azure/functions` v4, `node:test` (built-in, no extra deps), vanilla HTML/CSS/JS (no framework change).

**Spec:** `docs/superpowers/specs/2026-05-11-social-auth-sync-design.md`

---

## File Map

| File | Status | Responsibility |
|---|---|---|
| `api/host.json` | Create | Azure Functions host config |
| `api/package.json` | Create | Function dependencies (`@azure/functions`, `@azure/data-tables`) |
| `api/tags.js` | Create | HTTP handler registration (GET + PUT routes) |
| `api/lib/tagsFormat.js` | Create | Pure conversion helpers: `cloudToRuntime`, `runtimeToCloud`, `mergeTags`, `tagsAreEqual` |
| `api/lib/tagsFormat.test.js` | Create | Tests for format helpers |
| `api/lib/handlers.js` | Create | Business logic: `handleGet`, `handlePut`, `getUserId` |
| `api/lib/handlers.test.js` | Create | Tests for handlers (mock TableClient) |
| `public/staticwebapp.config.json` | Modify | Add auth providers + require auth on `/api/*` |
| `public/app.js` | Modify | Auth state vars, conversion helpers, sync functions, `setTag` wiring, conflict modal logic, header auth UI, bootstrap integration |
| `public/index.html` | Modify | Login button dropdown, user pill, sync indicator, conflict modal markup |
| `public/styles.css` | Modify | Auth UI styles, conflict modal styles |
| `.github/workflows/azure-static-web-apps.yml` | Modify | Set `api_location: "api"` |
| `public/sw.js` | Modify | Bump `CACHE_VERSION` from `pnw-v5` to `pnw-v6` |

---

## Task 1: API Function Scaffold

**Files:**
- Create: `api/host.json`
- Create: `api/package.json`
- Create: `api/lib/tagsFormat.js` (empty shell)
- Create: `api/lib/handlers.js` (empty shell)
- Create: `api/tags.js` (empty shell)
- Modify: `.github/workflows/azure-static-web-apps.yml`

- [ ] **Step 1: Create `api/host.json`**

```json
{
  "version": "2.0",
  "extensionBundle": {
    "id": "Microsoft.Azure.Functions.ExtensionBundle",
    "version": "[4.*, 5.0.0)"
  }
}
```

- [ ] **Step 2: Create `api/package.json`**

```json
{
  "name": "geekway-pnw-api",
  "version": "1.0.0",
  "private": true,
  "type": "commonjs",
  "main": "tags.js",
  "dependencies": {
    "@azure/data-tables": "^13.3.0",
    "@azure/functions": "^4.7.0"
  }
}
```

- [ ] **Step 3: Create empty shells**

Create `api/lib/tagsFormat.js`:
```js
'use strict';
// Populated in Task 2
module.exports = {};
```

Create `api/lib/handlers.js`:
```js
'use strict';
// Populated in Tasks 3–4
module.exports = {};
```

Create `api/tags.js`:
```js
'use strict';
// Populated in Task 4
const { app } = require('@azure/functions');
```

Create `api/lib/` directory (already implied by the files above).

- [ ] **Step 4: Update GitHub Actions workflow**

In `.github/workflows/azure-static-web-apps.yml`, change `api_location: ""` to `api_location: "api"`:

```yaml
          app_location: "public"
          api_location: "api"
          output_location: ""
```

- [ ] **Step 5: Install API dependencies**

```bash
cd api && npm install
```

Expected: `node_modules/@azure/functions` and `node_modules/@azure/data-tables` present.

- [ ] **Step 6: Commit**

```bash
git add api/ .github/workflows/azure-static-web-apps.yml
git commit -m "feat: scaffold SWA managed function for tag sync"
```

---

## Task 2: Tag Format Conversion Helpers + Tests

**Files:**
- Modify: `api/lib/tagsFormat.js`
- Create: `api/lib/tagsFormat.test.js`

**Context:** The cloud storage format is `{ want: [number], played: [number] }`. The runtime `TAGS` format (used in `app.js`) is `{ [gameId]: { want?: true, played?: true } }`. These helpers convert between them and are shared by both the API and `app.js`.

- [ ] **Step 1: Write the failing tests**

Create `api/lib/tagsFormat.test.js`:

```js
'use strict';
const { describe, it } = require('node:test');
const assert = require('node:assert/strict');
const { cloudToRuntime, runtimeToCloud, mergeTags, tagsAreEqual } = require('./tagsFormat.js');

describe('cloudToRuntime', () => {
  it('converts want/played arrays to lookup object', () => {
    const result = cloudToRuntime({ want: [1, 2], played: [2, 3] });
    assert.deepEqual(result, {
      1: { want: true },
      2: { want: true, played: true },
      3: { played: true },
    });
  });

  it('handles missing arrays gracefully', () => {
    assert.deepEqual(cloudToRuntime({}), {});
    assert.deepEqual(cloudToRuntime({ want: [1] }), { 1: { want: true } });
  });
});

describe('runtimeToCloud', () => {
  it('converts lookup object to want/played arrays', () => {
    const result = runtimeToCloud({ 1: { want: true }, 2: { played: true }, 3: { want: true, played: true } });
    assert.deepEqual(result.want.sort(), [1, 3].sort());
    assert.deepEqual(result.played.sort(), [2, 3].sort());
  });

  it('returns numeric ids (not strings)', () => {
    const result = runtimeToCloud({ 418059: { want: true } });
    assert.ok(result.want.every(id => typeof id === 'number'));
  });

  it('round-trips through cloudToRuntime', () => {
    const original = { want: [1, 2], played: [2, 3] };
    const result = runtimeToCloud(cloudToRuntime(original));
    assert.deepEqual(result.want.sort((a,b)=>a-b), original.want);
    assert.deepEqual(result.played.sort((a,b)=>a-b), original.played);
  });
});

describe('mergeTags', () => {
  it('unions both sets — tags from either side are kept', () => {
    const local  = { 1: { want: true }, 2: { played: true } };
    const cloud  = { 2: { want: true }, 3: { played: true } };
    const merged = mergeTags(local, cloud);
    assert.deepEqual(merged[1], { want: true });
    assert.deepEqual(merged[2], { want: true, played: true });
    assert.deepEqual(merged[3], { played: true });
  });

  it('handles empty inputs', () => {
    assert.deepEqual(mergeTags({}, { 1: { want: true } }), { 1: { want: true } });
    assert.deepEqual(mergeTags({ 1: { want: true } }, {}), { 1: { want: true } });
  });
});

describe('tagsAreEqual', () => {
  it('returns true for identical tags', () => {
    assert.ok(tagsAreEqual({ 1: { want: true } }, { 1: { want: true } }));
  });

  it('returns false when counts differ', () => {
    assert.ok(!tagsAreEqual({ 1: { want: true } }, {}));
  });

  it('returns false when tag values differ for same game', () => {
    assert.ok(!tagsAreEqual({ 1: { want: true } }, { 1: { played: true } }));
  });

  it('returns true for empty vs empty', () => {
    assert.ok(tagsAreEqual({}, {}));
  });
});
```

- [ ] **Step 2: Run tests — expect FAIL**

```bash
cd api && node --test lib/tagsFormat.test.js
```

Expected: failures because `tagsFormat.js` exports nothing yet.

- [ ] **Step 3: Implement `api/lib/tagsFormat.js`**

```js
'use strict';

/**
 * Convert compact cloud format { want: [ids], played: [ids] }
 * to runtime lookup { [gameId]: { want?: true, played?: true } }.
 */
function cloudToRuntime(cloud) {
  const tags = {};
  for (const id of cloud.want || []) {
    tags[id] = Object.assign(tags[id] || {}, { want: true });
  }
  for (const id of cloud.played || []) {
    tags[id] = Object.assign(tags[id] || {}, { played: true });
  }
  return tags;
}

/**
 * Convert runtime lookup to compact cloud format.
 * Game IDs are stored as numbers (not strings).
 */
function runtimeToCloud(tags) {
  const want = [], played = [];
  for (const [id, t] of Object.entries(tags)) {
    const n = Number(id);
    if (t.want)   want.push(n);
    if (t.played) played.push(n);
  }
  return { want, played };
}

/**
 * Union-merge two runtime tag objects.
 * Any tag present in either set is kept; no tags are removed.
 */
function mergeTags(a, b) {
  const result = {};
  for (const [id, t] of Object.entries(a)) {
    result[id] = { ...t };
  }
  for (const [id, t] of Object.entries(b)) {
    result[id] = Object.assign(result[id] || {}, t);
  }
  return result;
}

/**
 * Deep-equal check for two runtime tag objects.
 */
function tagsAreEqual(a, b) {
  const aKeys = Object.keys(a).sort();
  const bKeys = Object.keys(b).sort();
  if (aKeys.length !== bKeys.length) return false;
  return aKeys.every((k, i) => {
    if (k !== bKeys[i]) return false;
    const ta = a[k], tb = b[k];
    return !!ta.want === !!tb.want && !!ta.played === !!tb.played;
  });
}

module.exports = { cloudToRuntime, runtimeToCloud, mergeTags, tagsAreEqual };
```

- [ ] **Step 4: Run tests — expect PASS**

```bash
cd api && node --test lib/tagsFormat.test.js
```

Expected: all tests pass with `✓` marks.

- [ ] **Step 5: Commit**

```bash
git add api/lib/tagsFormat.js api/lib/tagsFormat.test.js
git commit -m "feat: add tag format conversion helpers with tests"
```

---

## Task 3: GET Handler + Tests

**Files:**
- Modify: `api/lib/handlers.js`
- Create: `api/lib/handlers.test.js`

**Context:** The handler reads the user's tags from Azure Table Storage. The entity has `tagsJson` (compact format JSON string) and `etag` (native Azure ETag — populated by `@azure/data-tables` on `getEntity`). `makeClient` is a factory injected for testing. `getUserId` decodes the base64 `x-ms-client-principal` header that SWA injects server-side.

- [ ] **Step 1: Write the failing GET tests**

Create `api/lib/handlers.test.js`:

```js
'use strict';
const { describe, it } = require('node:test');
const assert = require('node:assert/strict');

// Inline RestError mock (avoids requiring @azure/data-tables in test setup)
class RestError extends Error {
  constructor(msg, statusCode) {
    super(msg);
    this.statusCode = statusCode;
    this.name = 'RestError';
  }
}

// makeClient returns a factory () => mockTableClient
function makeClient({ existingEntity = null, postWriteEtag = 'W/"new"', updateShouldConflict = false } = {}) {
  let written = false;
  let writtenEntity = null;
  return () => ({
    async getEntity() {
      if (!existingEntity && !written) throw new RestError('not found', 404);
      if (written) return { ...writtenEntity, etag: postWriteEtag };
      return existingEntity;
    },
    async upsertEntity(entity) {
      writtenEntity = entity;
      written = true;
    },
    async updateEntity(entity, _mode, opts) {
      if (updateShouldConflict) throw new RestError('precondition failed', 412);
      writtenEntity = entity;
      written = true;
    },
    async createTable() {},
  });
}

// We'll import handleGet and handlePut once they exist
let handleGet, handlePut, getUserId;
try {
  ({ handleGet, handlePut, getUserId } = require('./handlers.js'));
} catch { /* will fail before implementation */ }

describe('getUserId', () => {
  it('returns null when header is missing', () => {
    const req = { headers: { get: () => null } };
    assert.equal(getUserId(req), null);
  });

  it('decodes the base64 principal and returns userId', () => {
    const principal = { userId: 'uid-abc', userDetails: 'user@example.com', identityProvider: 'google' };
    const encoded = Buffer.from(JSON.stringify(principal)).toString('base64');
    const req = { headers: { get: () => encoded } };
    assert.equal(getUserId(req), 'uid-abc');
  });

  it('returns null on invalid base64', () => {
    const req = { headers: { get: () => 'not-valid-json-base64!!!' } };
    assert.equal(getUserId(req), null);
  });
});

describe('handleGet', () => {
  it('returns 204 when no entity exists', async () => {
    const res = await handleGet('user1', makeClient({ existingEntity: null }));
    assert.equal(res.status, 204);
  });

  it('returns 200 with tags and etag when entity exists', async () => {
    const entity = {
      tagsJson: JSON.stringify({ want: [418059], played: [12345] }),
      etag: 'W/"abc123"',
    };
    const res = await handleGet('user1', makeClient({ existingEntity: entity }));
    assert.equal(res.status, 200);
    assert.deepEqual(res.jsonBody.tags, { want: [418059], played: [12345] });
    assert.equal(res.jsonBody.etag, 'W/"abc123"');
  });
});
```

- [ ] **Step 2: Run tests — expect FAIL (or load error)**

```bash
cd api && node --test lib/handlers.test.js
```

Expected: failures / import error because `handlers.js` is empty.

- [ ] **Step 3: Implement GET in `api/lib/handlers.js`**

```js
'use strict';
const { TableClient, RestError } = require('@azure/data-tables');

const TABLE_NAME = 'usertags';
const ROW_KEY    = 'tags';

function getDefaultClient() {
  const conn = process.env.AZURE_STORAGE_CONNECTION_STRING;
  if (!conn) throw new Error('AZURE_STORAGE_CONNECTION_STRING is not set');
  return TableClient.fromConnectionString(conn, TABLE_NAME);
}

function getUserId(request) {
  const header = request.headers.get('x-ms-client-principal');
  if (!header) return null;
  try {
    const decoded = JSON.parse(Buffer.from(header, 'base64').toString('utf-8'));
    return decoded.userId || null;
  } catch { return null; }
}

async function handleGet(userId, makeClient = getDefaultClient) {
  const client = makeClient();
  try {
    const entity = await client.getEntity(userId, ROW_KEY);
    return {
      status: 200,
      jsonBody: {
        tags: JSON.parse(entity.tagsJson),
        etag: entity.etag,
      },
    };
  } catch (err) {
    if (err instanceof RestError && err.statusCode === 404) return { status: 204 };
    throw err;
  }
}

module.exports = { getUserId, handleGet };
```

- [ ] **Step 4: Run GET tests — expect PASS**

```bash
cd api && node --test lib/handlers.test.js
```

Expected: `getUserId` and `handleGet` tests pass.

- [ ] **Step 5: Commit**

```bash
git add api/lib/handlers.js api/lib/handlers.test.js
git commit -m "feat: implement GET /api/tags handler with tests"
```

---

## Task 4: PUT Handler + Tests + HTTP Registration

**Files:**
- Modify: `api/lib/handlers.js` (add `handlePut`)
- Modify: `api/lib/handlers.test.js` (add PUT tests)
- Modify: `api/tags.js` (wire up HTTP endpoints)

**Context:** PUT uses Azure's native `If-Match` eTag for optimistic concurrency. When `clientEtag` is `null` (first write), `upsertEntity` is used with no condition. When `clientEtag` is provided, `updateEntity` is called with `{ etag: clientEtag }`; a 412 from Azure becomes a 409 to the client. After a successful write, `getEntity` is called once to retrieve the new eTag.

- [ ] **Step 1: Add PUT tests to `api/lib/handlers.test.js`**

Append to the existing test file after the `handleGet` describe block:

```js
describe('handlePut', () => {
  it('uses upsert on first write (null etag) and returns new etag', async () => {
    const client = makeClient({ existingEntity: null, postWriteEtag: 'W/"new123"' });
    const res = await handlePut('user1', { want: [1], played: [] }, null, client);
    assert.equal(res.status, 200);
    assert.equal(res.jsonBody.etag, 'W/"new123"');
  });

  it('uses conditional update when etag is provided', async () => {
    let capturedEtag;
    const client = () => ({
      async updateEntity(_entity, _mode, opts) { capturedEtag = opts.etag; },
      async getEntity()  { return { tagsJson: '{"want":[],"played":[]}', etag: 'W/"updated"' }; },
      async createTable() {},
    });
    const res = await handlePut('user1', { want: [1], played: [] }, 'W/"old"', client);
    assert.equal(res.status, 200);
    assert.equal(capturedEtag, 'W/"old"');
    assert.equal(res.jsonBody.etag, 'W/"updated"');
  });

  it('returns 409 when Azure rejects the etag (412)', async () => {
    const client = makeClient({ updateShouldConflict: true });
    const res = await handlePut('user1', { want: [1], played: [] }, 'W/"stale"', client);
    assert.equal(res.status, 409);
  });
});
```

- [ ] **Step 2: Run tests — PUT tests should FAIL**

```bash
cd api && node --test lib/handlers.test.js
```

Expected: `handlePut` tests fail because `handlePut` is not exported yet.

- [ ] **Step 3: Add `handlePut` to `api/lib/handlers.js`**

Append before `module.exports`:

```js
async function handlePut(userId, tags, clientEtag, makeClient = getDefaultClient) {
  const client = makeClient();
  const entity = {
    partitionKey: userId,
    rowKey:       ROW_KEY,
    tagsJson:     JSON.stringify(tags),
  };
  try {
    if (clientEtag == null) {
      await client.upsertEntity(entity, 'Replace');
    } else {
      await client.updateEntity(entity, 'Replace', { etag: clientEtag });
    }
    const updated = await client.getEntity(userId, ROW_KEY);
    return { status: 200, jsonBody: { etag: updated.etag } };
  } catch (err) {
    if (err instanceof RestError && err.statusCode === 412) {
      return { status: 409, jsonBody: { error: 'conflict' } };
    }
    throw err;
  }
}
```

Update `module.exports` at the bottom of `handlers.js`:

```js
module.exports = { getUserId, handleGet, handlePut };
```

- [ ] **Step 4: Run all handler tests — expect PASS**

```bash
cd api && node --test lib/handlers.test.js
```

Expected: all 8 tests pass.

- [ ] **Step 5: Wire up HTTP endpoints in `api/tags.js`**

Replace the contents of `api/tags.js`:

```js
'use strict';
const { app } = require('@azure/functions');
const { getUserId, handleGet, handlePut } = require('./lib/handlers.js');

app.http('tags', {
  methods:   ['GET', 'PUT'],
  authLevel: 'anonymous',   // SWA route rules enforce authentication before the function runs
  route:     'tags',
  handler: async (request, _context) => {
    const userId = getUserId(request);
    if (!userId) return { status: 401, jsonBody: { error: 'unauthenticated' } };

    if (request.method === 'GET') {
      return handleGet(userId);
    }

    if (request.method === 'PUT') {
      let body;
      try { body = await request.json(); } catch { return { status: 400, jsonBody: { error: 'invalid JSON' } }; }
      const { tags, etag } = body || {};
      if (!tags || typeof tags !== 'object') return { status: 400, jsonBody: { error: 'missing tags' } };
      return handlePut(userId, tags, etag ?? null);
    }

    return { status: 405 };
  },
});
```

- [ ] **Step 6: Commit**

```bash
git add api/lib/handlers.js api/lib/handlers.test.js api/tags.js
git commit -m "feat: implement PUT /api/tags handler with eTag concurrency + HTTP registration"
```

---

## Task 5: Auth Config + Route Protection

**Files:**
- Modify: `public/staticwebapp.config.json`

**Context:** SWA's `auth.identityProviders` block configures Google (built-in) and Apple (via custom OpenID Connect). Client IDs/secrets are referenced by environment variable name — the actual values are set in the Azure portal's "Environment Variables" for the SWA resource. The route rule `allowedRoles: ["authenticated"]` on `/api/*` rejects unauthenticated requests before the function runs.

- [ ] **Step 1: Replace `public/staticwebapp.config.json`**

```json
{
  "navigationFallback": {
    "rewrite": "/index.html"
  },
  "globalHeaders": {
    "Cache-Control": "public, max-age=3600"
  },
  "mimeTypes": {
    ".json": "application/json"
  },
  "auth": {
    "identityProviders": {
      "google": {
        "registration": {
          "clientIdSettingName":     "GOOGLE_CLIENT_ID",
          "clientSecretSettingName": "GOOGLE_CLIENT_SECRET"
        }
      },
      "customOpenIdConnectProviders": {
        "apple": {
          "registration": {
            "clientIdSettingName":     "APPLE_CLIENT_ID",
            "clientSecretSettingName": "APPLE_CLIENT_SECRET",
            "openIdConnectConfiguration": {
              "wellKnownOpenIdConfiguration": "https://appleid.apple.com/.well-known/openid-configuration"
            }
          },
          "login": {
            "nameClaimType": "email",
            "scopes": ["email", "name"]
          }
        }
      }
    }
  },
  "routes": [
    {
      "route": "/api/*",
      "allowedRoles": ["authenticated"]
    }
  ],
  "responseOverrides": {
    "401": {
      "statusCode": 401,
      "redirect": ""
    }
  }
}
```

> **Note:** The `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `APPLE_CLIENT_ID`, and `APPLE_CLIENT_SECRET` env vars must be set in the Azure portal before login works. See the One-Time Setup Checklist in the spec.

- [ ] **Step 2: Commit**

```bash
git add public/staticwebapp.config.json
git commit -m "feat: add Google + Apple auth providers and /api/* route protection"
```

---

## Task 6: app.js — Tag Conversion Helpers + Auth State

**Files:**
- Modify: `public/app.js`

**Context:** The same format helpers from `api/lib/tagsFormat.js` are needed client-side in `app.js`. They're duplicated here (not imported) because there's no bundler — the site is plain JS. Add them near the existing `// ─── Tags (shortlist)` section.

- [ ] **Step 1: Add auth state variables and conversion helpers to `app.js`**

Insert after the existing `// ─── Tags (shortlist) ──` block (after `setTag` function, before `// ─── URL / localStorage state`):

```js
// ─── Auth + sync state ────────────────────────────────────────────────────────

let AUTH_USER   = null;  // { userId, userDetails, identityProvider } | null
let CLOUD_ETAG  = null;  // eTag of the last successfully read/written cloud entity
let SYNC_STATUS = 'idle'; // 'idle' | 'syncing' | 'synced' | 'offline'

// Cloud format: { want: [ids], played: [ids] }
// Runtime format (TAGS): { [gameId]: { want?: true, played?: true } }

function cloudToRuntime(cloud) {
  const tags = {};
  for (const id of cloud.want   || []) tags[id] = Object.assign(tags[id] || {}, { want:   true });
  for (const id of cloud.played || []) tags[id] = Object.assign(tags[id] || {}, { played: true });
  return tags;
}

function runtimeToCloud(tags) {
  const want = [], played = [];
  for (const [id, t] of Object.entries(tags)) {
    const n = Number(id);
    if (t.want)   want.push(n);
    if (t.played) played.push(n);
  }
  return { want, played };
}

function mergeTags(a, b) {
  const result = {};
  for (const [id, t] of Object.entries(a)) result[id] = { ...t };
  for (const [id, t] of Object.entries(b)) result[id] = Object.assign(result[id] || {}, t);
  return result;
}

function tagsAreEqual(a, b) {
  const aKeys = Object.keys(a).sort();
  const bKeys = Object.keys(b).sort();
  if (aKeys.length !== bKeys.length) return false;
  return aKeys.every((k, i) => {
    if (k !== bKeys[i]) return false;
    return !!a[k].want === !!b[k].want && !!a[k].played === !!b[k].played;
  });
}

async function checkAuth() {
  try {
    const res = await fetch('/.auth/me');
    if (!res.ok) return null;
    const data = await res.json();
    AUTH_USER = data.clientPrincipal || null;
    return AUTH_USER;
  } catch {
    AUTH_USER = null;
    return null;
  }
}
```

- [ ] **Step 2: Verify no syntax errors**

```bash
cd C:\Projects\geekway-play2win && npm run dev
```

Open `http://localhost:3000` — the page should load and function identically to before (no visual change yet).

- [ ] **Step 3: Commit**

```bash
git add public/app.js
git commit -m "feat: add auth state vars and tag format helpers to app.js"
```

---

## Task 7: app.js — Sync Functions + setTag Wiring

**Files:**
- Modify: `public/app.js`

**Context:** `setSyncStatus` updates a DOM element added in Task 9 (not present yet — reference the element ID `syncStatus` but guard with `?.`). `syncToCloud` is debounced 1.5 s. On a 409, it re-fetches, union-merges, and retries once. `syncFromCloud` is called at page load; it handles the no-conflict fast path here — the conflict modal is wired in Task 8.

- [ ] **Step 1: Add sync functions after the auth state block from Task 6**

Insert immediately after the `checkAuth` function:

```js
function setSyncStatus(status) {
  SYNC_STATUS = status;
  const el = document.getElementById('syncStatus');
  if (!el) return;
  el.dataset.status = status;
  el.textContent = status === 'syncing' ? '↻ Syncing…'
                 : status === 'synced'  ? '☁ Synced'
                 : status === 'offline' ? '⚠ Offline'
                 : '';
  el.hidden = (status === 'idle');
}

let _syncTimer = null;

function scheduleSyncToCloud() {
  if (!AUTH_USER) return;
  clearTimeout(_syncTimer);
  _syncTimer = setTimeout(syncToCloud, 1500);
}

async function syncToCloud() {
  if (!AUTH_USER) return;
  setSyncStatus('syncing');
  try {
    const res = await fetch('/api/tags', {
      method:  'PUT',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ tags: runtimeToCloud(TAGS), etag: CLOUD_ETAG }),
    });
    if (res.status === 409) {
      // Stale eTag — re-fetch, merge, retry once
      const getRes = await fetch('/api/tags');
      if (!getRes.ok || getRes.status === 204) { setSyncStatus('offline'); return; }
      const { tags: freshCloud, etag: freshEtag } = await getRes.json();
      CLOUD_ETAG = freshEtag;
      TAGS = mergeTags(TAGS, cloudToRuntime(freshCloud));
      saveTags();
      const retryRes = await fetch('/api/tags', {
        method:  'PUT',
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify({ tags: runtimeToCloud(TAGS), etag: CLOUD_ETAG }),
      });
      if (retryRes.ok) {
        const { etag } = await retryRes.json();
        CLOUD_ETAG = etag;
        setSyncStatus('synced');
      } else {
        setSyncStatus('offline');
      }
      return;
    }
    if (!res.ok) { setSyncStatus('offline'); return; }
    const { etag } = await res.json();
    CLOUD_ETAG = etag;
    setSyncStatus('synced');
  } catch { setSyncStatus('offline'); }
}

// Resolves after syncing from cloud (or gracefully failing).
// Returns true if a conflict modal was shown and resolved.
async function syncFromCloud() {
  if (!AUTH_USER) return false;
  setSyncStatus('syncing');
  try {
    const res = await fetch('/api/tags');
    if (!res.ok) { setSyncStatus('offline'); return false; }

    if (res.status === 204) {
      // Nothing in cloud — push local if any
      if (Object.keys(TAGS).length > 0) await syncToCloud();
      else setSyncStatus('synced');
      return false;
    }

    const { tags: cloudCompact, etag } = await res.json();
    CLOUD_ETAG = etag;
    const cloudRuntime = cloudToRuntime(cloudCompact);
    const localEmpty   = Object.keys(TAGS).length === 0;
    const cloudEmpty   = Object.keys(cloudRuntime).length === 0;

    if (localEmpty) {
      TAGS = cloudRuntime; saveTags(); setSyncStatus('synced'); return false;
    }
    if (cloudEmpty) {
      await syncToCloud(); return false;
    }
    if (tagsAreEqual(TAGS, cloudRuntime)) {
      setSyncStatus('synced'); return false;
    }

    // Both sides non-empty and diverged — show conflict modal (wired in Task 8)
    const resolved = await showConflictModal(TAGS, cloudRuntime);
    TAGS = resolved;
    saveTags();
    await syncToCloud();
    return true;
  } catch { setSyncStatus('offline'); return false; }
}
```

- [ ] **Step 2: Modify `setTag` to schedule a cloud sync**

Find the existing `setTag` function:
```js
function setTag(id, tag, on) {
  const t = TAGS[id] || (TAGS[id] = {});
  if (on) t[tag] = true;
  else {
    delete t[tag];
    if (Object.keys(t).length === 0) delete TAGS[id];
  }
  saveTags();
}
```

Replace with:
```js
function setTag(id, tag, on) {
  const t = TAGS[id] || (TAGS[id] = {});
  if (on) t[tag] = true;
  else {
    delete t[tag];
    if (Object.keys(t).length === 0) delete TAGS[id];
  }
  saveTags();
  scheduleSyncToCloud();
}
```

- [ ] **Step 3: Add a stub for `showConflictModal` (implemented in Task 8)**

Insert immediately before `syncFromCloud`:

```js
// Stub — replaced in Task 8 with real modal logic
async function showConflictModal(localTags, cloudTags) {
  // Default: merge both until the real modal is implemented
  return mergeTags(localTags, cloudTags);
}
```

- [ ] **Step 4: Verify no syntax errors**

```bash
npm run dev
```

Open `http://localhost:3000` — page loads normally. Tag buttons still work (localStorage only, since auth not wired yet).

- [ ] **Step 5: Commit**

```bash
git add public/app.js
git commit -m "feat: add cloud sync functions and wire setTag to scheduleSyncToCloud"
```

---

## Task 8: Conflict Modal

**Files:**
- Modify: `public/index.html`
- Modify: `public/styles.css`
- Modify: `public/app.js`

**Context:** The modal is a `<dialog>` element positioned as a centered overlay. It shows the count of tagged games on each side and three buttons. `showConflictModal` returns a Promise that resolves with the chosen tag set. Replace the stub from Task 7.

- [ ] **Step 1: Add modal HTML to `public/index.html`**

Insert before the closing `</div>` of `.page` (just before the `<footer>` element):

```html
  <dialog id="conflictModal" class="conflict-modal">
    <h2 class="conflict-modal-title">Sync Conflict</h2>
    <p class="conflict-modal-desc" id="conflictDesc"></p>
    <div class="conflict-modal-actions">
      <button type="button" class="btn" id="conflictCloud">Keep cloud</button>
      <button type="button" class="btn" id="conflictLocal">Use this device</button>
      <button type="button" class="btn btn-accent" id="conflictMerge">Merge both</button>
    </div>
  </dialog>
```

- [ ] **Step 2: Add modal CSS to `public/styles.css`**

Append to the end of `styles.css`:

```css
/* ─── Conflict modal ─────────────────────────────────────────────────────── */

.conflict-modal {
  position: fixed;
  inset: 0;
  margin: auto;
  width: min(440px, calc(100vw - 32px));
  padding: 24px;
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  background: var(--bg);
  box-shadow: 0 16px 48px rgba(0, 0, 0, 0.2);
  z-index: 500;
}

.conflict-modal::backdrop {
  background: rgba(0, 0, 0, 0.4);
}

.conflict-modal-title {
  font-size: 16px;
  font-weight: 600;
  margin: 0 0 10px;
  color: var(--text);
}

.conflict-modal-desc {
  font-size: 13px;
  color: var(--text-2);
  margin: 0 0 20px;
  line-height: 1.6;
}

.conflict-modal-actions {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  justify-content: flex-end;
}

.btn-accent {
  background: var(--accent);
  border-color: var(--accent);
  color: #fff;
}
.btn-accent:hover { opacity: 0.9; background: var(--accent); }
```

- [ ] **Step 3: Replace the `showConflictModal` stub in `app.js`**

Find and replace the stub:

```js
// Stub — replaced in Task 8 with real modal logic
async function showConflictModal(localTags, cloudTags) {
  // Default: merge both until the real modal is implemented
  return mergeTags(localTags, cloudTags);
}
```

Replace with:

```js
function showConflictModal(localTags, cloudTags) {
  return new Promise(resolve => {
    const modal   = document.getElementById('conflictModal');
    const desc    = document.getElementById('conflictDesc');
    const btnCloud = document.getElementById('conflictCloud');
    const btnLocal = document.getElementById('conflictLocal');
    const btnMerge = document.getElementById('conflictMerge');

    const localCount = Object.keys(localTags).length;
    const cloudCount = Object.keys(cloudTags).length;
    desc.textContent =
      `This device has tags for ${localCount} game${localCount !== 1 ? 's' : ''}. ` +
      `Your account has tags for ${cloudCount} game${cloudCount !== 1 ? 's' : ''}. ` +
      `What should we do?`;

    function finish(result) {
      modal.close();
      btnCloud.removeEventListener('click', onCloud);
      btnLocal.removeEventListener('click', onLocal);
      btnMerge.removeEventListener('click', onMerge);
      resolve(result);
    }
    const onCloud = () => finish(cloudTags);
    const onLocal = () => finish(localTags);
    const onMerge = () => finish(mergeTags(localTags, cloudTags));

    btnCloud.addEventListener('click', onCloud);
    btnLocal.addEventListener('click', onLocal);
    btnMerge.addEventListener('click', onMerge);

    modal.showModal();
  });
}
```

- [ ] **Step 4: Verify modal renders**

```bash
npm run dev
```

Open browser console and run:
```js
showConflictModal({ 1: { want: true } }, { 2: { played: true } })
  .then(r => console.log('resolved:', r));
```

Expected: modal appears with "This device has tags for 1 game. Your account has tags for 1 game." — clicking each button resolves the promise and closes the modal.

- [ ] **Step 5: Commit**

```bash
git add public/index.html public/styles.css public/app.js
git commit -m "feat: add conflict resolution modal"
```

---

## Task 9: Header Auth UI

**Files:**
- Modify: `public/index.html`
- Modify: `public/styles.css`
- Modify: `public/app.js`

**Context:** The existing `.header-btns` div holds the install and theme buttons. A "Sign in" button (with a small provider dropdown) and a logged-in user pill are added to the left of the theme button. `updateAuthUI(user)` is called with the `AUTH_USER` object (or `null`) to toggle the two states.

- [ ] **Step 1: Add auth UI markup to `public/index.html`**

Find the `.header-btns` div:
```html
    <div class="header-btns">
      <button type="button" class="btn icon-btn install-btn" id="installBtn" aria-label="Install app" title="Install app" hidden>⬇</button>
      <button type="button" class="btn icon-btn" id="themeBtn" aria-label="Toggle theme" title="Toggle light/dark">🌙</button>
    </div>
```

Replace with:
```html
    <div class="header-btns">
      <button type="button" class="btn icon-btn install-btn" id="installBtn" aria-label="Install app" title="Install app" hidden>⬇</button>

      <!-- Logged-out: sign-in button + provider dropdown -->
      <div class="auth-signin" id="authSignIn">
        <button type="button" class="btn auth-signin-btn" id="signInBtn" aria-haspopup="true" aria-expanded="false">Sign in</button>
        <ul class="auth-provider-menu" id="authProviderMenu" hidden role="menu">
          <li><a href="/.auth/login/google?post_login_redirect_uri=/" role="menuitem">Sign in with Google</a></li>
          <li><a href="/.auth/login/apple?post_login_redirect_uri=/" role="menuitem">Sign in with Apple</a></li>
        </ul>
      </div>

      <!-- Logged-in: user pill + sync status + sign-out -->
      <div class="auth-user" id="authUser" hidden>
        <span class="auth-avatar" id="authAvatar" aria-hidden="true"></span>
        <span class="auth-name" id="authName"></span>
        <span class="auth-sync" id="syncStatus" hidden></span>
        <a href="/.auth/logout?post_logout_redirect_uri=/" class="auth-signout">Sign out</a>
      </div>

      <button type="button" class="btn icon-btn" id="themeBtn" aria-label="Toggle theme" title="Toggle light/dark">🌙</button>
    </div>
```

- [ ] **Step 2: Add auth UI CSS to `public/styles.css`**

Append to `styles.css`:

```css
/* ─── Auth UI (header) ───────────────────────────────────────────────────── */

.auth-signin { position: relative; }

.auth-signin-btn {
  font-size: 12px;
  height: 30px;
  padding: 0 10px;
}

.auth-provider-menu {
  position: absolute;
  top: calc(100% + 4px);
  right: 0;
  margin: 0;
  padding: 4px 0;
  list-style: none;
  min-width: 180px;
  background: var(--bg);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.15);
  z-index: 300;
}

.auth-provider-menu li a {
  display: block;
  padding: 8px 14px;
  font-size: 13px;
  color: var(--text);
  text-decoration: none;
  white-space: nowrap;
}

.auth-provider-menu li a:hover { background: var(--bg-hover); }

.auth-user {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  color: var(--text-2);
}

.auth-avatar {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  border-radius: 50%;
  background: var(--accent-bg);
  color: var(--accent);
  font-size: 11px;
  font-weight: 700;
  flex-shrink: 0;
}

.auth-name {
  max-width: 100px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.auth-sync {
  font-size: 11px;
  font-family: 'Geist Mono', ui-monospace, monospace;
  color: var(--text-3);
}

.auth-sync[data-status="syncing"] { color: var(--accent); }
.auth-sync[data-status="offline"]  { color: #f97316; }

.auth-signout {
  color: var(--text-3);
  font-size: 11px;
  text-decoration: none;
}
.auth-signout:hover { color: var(--text); text-decoration: underline; }

.auth-user[hidden],
.auth-signin[hidden] { display: none; }

@media (max-width: 480px) {
  .auth-name { display: none; }
}
```

- [ ] **Step 3: Add `updateAuthUI` to `app.js`**

Insert after the `checkAuth` function (before the `setSyncStatus` block from Task 7):

```js
function updateAuthUI(user) {
  const signInEl = document.getElementById('authSignIn');
  const userEl   = document.getElementById('authUser');
  const nameEl   = document.getElementById('authName');
  const avatarEl = document.getElementById('authAvatar');
  if (!signInEl || !userEl) return;

  if (!user) {
    signInEl.hidden = false;
    userEl.hidden   = true;
    return;
  }

  const display = user.userDetails || user.userId || '';
  nameEl.textContent   = display.length > 24 ? display.slice(0, 22) + '…' : display;
  avatarEl.textContent = (display[0] || '?').toUpperCase();
  signInEl.hidden = true;
  userEl.hidden   = false;
}
```

- [ ] **Step 4: Add sign-in dropdown toggle to `app.js`**

In the existing event-wiring section (near the other button listeners), append:

```js
// Sign-in dropdown toggle
const signInBtn       = document.getElementById('signInBtn');
const authProviderMenu = document.getElementById('authProviderMenu');

if (signInBtn && authProviderMenu) {
  signInBtn.addEventListener('click', e => {
    e.stopPropagation();
    const open = authProviderMenu.hidden;
    authProviderMenu.hidden = !open;
    signInBtn.setAttribute('aria-expanded', String(open));
  });

  document.addEventListener('click', e => {
    if (!authProviderMenu.hidden && !e.target.closest('.auth-signin')) {
      authProviderMenu.hidden = true;
      signInBtn.setAttribute('aria-expanded', 'false');
    }
  });
}
```

- [ ] **Step 5: Verify UI**

```bash
npm run dev
```

Open `http://localhost:3000` — the header should show a "Sign in" button. Clicking it shows the Google/Apple dropdown. (Links won't work locally without SWA auth.)

- [ ] **Step 6: Commit**

```bash
git add public/index.html public/styles.css public/app.js
git commit -m "feat: add auth sign-in/user-pill header UI"
```

---

## Task 10: Bootstrap Integration

**Files:**
- Modify: `public/app.js`

**Context:** `checkAuth()` is added to the `Promise.all` data-load call so it runs in parallel with the game/mechanics/categories fetches. After data loads, if a user is logged in, `syncFromCloud()` runs before `render()`. `updateAuthUI` is called once auth state is known. The existing `render()` call at the end of `.then()` is replaced with an async sequence.

- [ ] **Step 1: Replace the bootstrap `Promise.all` block**

Find the existing bootstrap block at the bottom of `app.js`:

```js
Promise.all([
  fetch('games.json').then(r => r.json()),
  fetch('mechanics.json').then(r => r.json()).catch(() => []),
  fetch('categories.json').then(r => r.json()).catch(() => []),
]).then(([games, mechanics, categories]) => {
```

Replace the entire `Promise.all(...).then(...)` call (through the closing `.catch(err => { ... })`) with:

```js
Promise.all([
  fetch('games.json').then(r => r.json()),
  fetch('mechanics.json').then(r => r.json()).catch(() => []),
  fetch('categories.json').then(r => r.json()).catch(() => []),
  checkAuth(),
]).then(async ([games, mechanics, categories]) => {
  MECHANICS  = mechanics;
  CATEGORIES = categories;

  DATA = games.map(r => ({
    ...r,
    bestPlayers:        r.bestPlayers        || [],
    recommendedPlayers: r.recommendedPlayers || [],
    mechanics:          r.mechanics          || [],
    categories:         r.categories         || [],
    maxTime: r.maxTime != null ? r.maxTime : null,
  }));

  availableSubranks = Object.keys(SUB_RANK_LABELS).filter(k =>
    DATA.some(r => r.subRanks?.[k] != null)
  );

  renderExtraColHeaders();

  initMultiSelect({
    container: mechanicSelect,
    input:     mechanicInput,
    dropdown:  mechanicDropdown,
    modeBtn:   mechanicModeBtn,
    stateKey:  'mechanics',
    modeKey:   'mechanicsMode',
    allItemsFn: () => MECHANICS,
  });

  initMultiSelect({
    container: categorySelect,
    input:     categoryInput,
    dropdown:  categoryDropdown,
    modeBtn:   categoryModeBtn,
    stateKey:  'categories',
    modeKey:   'categoriesMode',
    allItemsFn: () => CATEGORIES,
  });

  // Update header to reflect login state
  updateAuthUI(AUTH_USER);

  // Sync from cloud before first render (silently continues on error)
  if (AUTH_USER) {
    try { await syncFromCloud(); } catch { setSyncStatus('offline'); }
  }

  render();
}).catch(err => {
  tbody.innerHTML = '<tr><td colspan="13" class="loading">Failed to load data: ' + escapeHtml(err.message) + '</td></tr>';
});
```

- [ ] **Step 2: Verify full bootstrap sequence**

```bash
npm run dev
```

Open browser. Expected behavior:
- Page loads and renders the game table (unchanged from before).
- `AUTH_USER` is `null` (no SWA auth locally), so "Sign in" button shows.
- No console errors.

To simulate a logged-in state manually, open the browser console and run:
```js
AUTH_USER = { userId: 'test-uid', userDetails: 'test@example.com', identityProvider: 'google' };
updateAuthUI(AUTH_USER);
```
Expected: header switches to the user pill showing "T" avatar and "test@example.com".

- [ ] **Step 3: Commit**

```bash
git add public/app.js
git commit -m "feat: wire auth check and cloud sync into bootstrap sequence"
```

---

## Task 11: Service Worker Cache Version Bump

**Files:**
- Modify: `public/sw.js`

**Context:** `app.js`, `index.html`, and `styles.css` have all changed. The service worker pre-caches these files and uses `CACHE_VERSION` to detect when to invalidate the cache. Bumping it forces clients to re-fetch all cached assets on next load.

- [ ] **Step 1: Bump `CACHE_VERSION` in `public/sw.js`**

Find:
```js
const CACHE_VERSION = 'pnw-v5';
```

Replace with:
```js
const CACHE_VERSION = 'pnw-v6';
```

- [ ] **Step 2: Commit**

```bash
git add public/sw.js
git commit -m "chore: bump service worker cache version to pnw-v6"
```

---

## End-to-End Verification Checklist

After all tasks are complete, verify the following against a live SWA deployment (or `swa start` with credentials configured):

- [ ] Unauthenticated user sees "Sign in" button; all existing features work identically to before
- [ ] Clicking "Sign in" shows Google/Apple dropdown
- [ ] After Google login: user pill shows initial + truncated email; sync status shows "☁ Synced"
- [ ] After Apple login: same behavior
- [ ] Tagging a game ♥ or ✓ → sync status briefly shows "↻ Syncing…" then "☁ Synced"
- [ ] Loading the page on a second device (same account): tags from first device appear
- [ ] Offline: tag a game with no network → status shows "⚠ Offline"; tag is in localStorage
- [ ] Conflict modal: tag games on two devices while logged out, log in on each → modal appears on second device with correct counts
- [ ] "Merge both" → both sets of tags present after merge
- [ ] "Keep cloud" → local overwritten with cloud
- [ ] "Use this device" → cloud overwritten with local
- [ ] Logout: user pill replaced by "Sign in"; tags remain in localStorage
