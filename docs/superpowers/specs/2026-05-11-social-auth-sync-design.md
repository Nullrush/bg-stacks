# Social Auth + Tag Sync — Design Spec
*Date: 2026-05-11*

## Problem

Users currently store "want to play" and "played" game tags in `localStorage`, which is device-specific. There is no way to see those tags on a second device (e.g., tagged games on desktop, can't see them on a phone at the convention).

## Solution

Add social login (Google + Apple) via Azure Static Web Apps built-in authentication, backed by a single SWA Managed Function and Azure Table Storage. Logged-in users have their tags synced to the cloud and available on any device.

---

## Architecture

```
Browser (app.js)
  ├─ /.auth/login/google     ← SWA handles full OAuth flow
  ├─ /.auth/login/apple      ← SWA handles full OAuth flow
  ├─ /.auth/me               ← returns stable userId + display info
  └─ /api/tags               ← SWA Managed Function (GET / PUT)
                                  └─ Azure Table Storage
                                       Partition: userId
                                       Row:       "tags"
                                       Value:     JSON blob
```

### What is new

| Layer | Change |
|---|---|
| `staticwebapp.config.json` | Auth provider config (Google, Apple) + require auth on `/api/*` |
| `api/tags.js` | New SWA Managed Function — GET/PUT user tags |
| `public/app.js` | Auth check on load, sync on tag changes, conflict modal logic, sync status indicator |
| `public/index.html` | Login/user button in header |
| `public/styles.css` | Login button, user pill, sync indicator, conflict modal styles |
| Azure portal (one-time) | Create Storage Account, add connection string to SWA env vars |
| Google Cloud Console (one-time) | Register OAuth app, get client ID/secret |
| Apple Developer Portal (one-time) | Register Sign In with Apple Service ID + credentials |

### What is unchanged

All existing `localStorage` behavior is preserved as a fallback. Logged-out users experience zero change. The tag buttons (♥ / ✓) work exactly as before.

---

## Auth Flow

**Login:**
User clicks "Sign in" → redirected to `/.auth/login/google?post_login_redirect_uri=/` or `/.auth/login/apple?post_login_redirect_uri=/`. SWA handles the OAuth exchange. On return, `/.auth/me` returns `{ clientPrincipal: { userId, userDetails, identityProvider } }`.

**On page load (logged in):**
1. `app.js` calls `/.auth/me` — if authenticated, fetches `GET /api/tags`
2. Cloud tags are compared to localStorage tags
3. If a conflict exists (both non-empty and different), the user is prompted (see Conflict Resolution below)
4. If no conflict, whichever side is non-empty wins; merged result is saved to both localStorage and cloud

**Tag changes while logged in:**
Every `setTag()` call triggers a debounced `PUT /api/tags` with the full tags object. Simple replace — no diff/patch needed at this scale.

**Logout:**
`/.auth/logout` — SWA invalidates the session. Local tags remain in localStorage (user keeps offline data).

---

## Conflict Resolution

On first login from a device that already has local tags, if both local and cloud have non-empty and differing tags, a modal appears:

> *"This device has tags for N games. Your account has tags for M games. What should we do?"*
>
> **[ Keep cloud ]  [ Use this device ]  [ Merge both ]**

- **Keep cloud:** Cloud tags replace local tags. Local changes are discarded.
- **Use this device:** Local tags are pushed to cloud, overwriting cloud.
- **Merge both:** Union of all tag entries. If a game appears in both, all tags present in either set are kept (no tags are removed).

After the choice, the result is saved to both localStorage and cloud. On subsequent page loads, if local tags equal cloud tags, no prompt is shown. The conflict modal only appears when a real divergence exists — for example, if the user tagged games while logged out and then logs back in.

**Note:** Concurrent writes from two active devices are handled silently via the eTag retry loop (see Data Design). The conflict modal is only for the offline-divergence case, not for simultaneous multi-device use.

---

## Data Design

**Azure Table Storage schema:**

```
Table name: usertags
  PartitionKey: userId         (from SWA's /.auth/me, injected server-side — tamper-proof)
  RowKey:       "tags"
  Value:        '{"want":[418059,67890],"played":[12345]}'
  Timestamp:    (auto-managed)
```

**Storage format (compact):**

Tags are stored in a compact array format rather than the runtime per-game-object format. This is ~66% smaller and more semantic:

```json
{ "want": [418059, 67890], "played": [12345, 54321] }
```

On load, `app.js` converts this to the runtime `TAGS` lookup object: `{ [id]: { want?: true, played?: true } }`. On save, it converts back. Both conversions are trivial O(n).

**eTag — optimistic concurrency:**

Azure Table Storage returns an `ETag` on every GET/PUT. The API exposes this to the client to prevent lost updates when two devices are active simultaneously (e.g., desktop in the morning, phone at the convention):

1. `GET /api/tags` returns the current eTag alongside tags
2. Client stores the eTag in memory
3. `PUT /api/tags` sends the client's stored eTag in the request body
4. The function passes it as `If-Match` to Table Storage
5. If Table Storage returns `412 Precondition Failed` (another device wrote first), the function returns `409 Conflict` to the client
6. Client auto-retries: re-fetch → union merge → PUT — no user prompt needed for concurrent writes

**API surface (`api/tags.js`):**

```
GET  /api/tags
  → 200 { tags: { want: [ids], played: [ids] }, etag: "W/\"...\"" }
  → 204 (no tags stored yet)

PUT  /api/tags
  body: { tags: { want: [ids], played: [ids] }, etag: "W/\"...\"" | null }
  → 200 { etag: "W/\"...\"" }   (new eTag after successful write)
  → 409 Conflict                (stale eTag — client should re-fetch and retry)
```

`etag: null` is accepted on the first PUT (no prior record exists); the function uses `If-None-Match: *` in that case (fail if a row already exists — avoids a race condition on first write).

Both endpoints extract `userId` from the `x-ms-client-principal` header that SWA injects. A user can never read or write another user's row.

**Error handling:**
If `/api/tags` fails (network error, cold start, etc.), the app silently falls back to localStorage-only mode. A sync status indicator shows the user their current state without blocking the UI.

---

## UI Changes

All changes are additive. The page layout is not restructured.

**Header — logged out:**
- A "Sign in" button (right side, near the theme toggle)
- Clicking shows a small dropdown: "Sign in with Google" / "Sign in with Apple"

**Header — logged in:**
- User initial/avatar + display name (truncated)
- Sync status: "☁ Synced" / "↻ Syncing…" / "⚠ Offline"
- "Sign out" link

**Conflict modal:**
Centered overlay (styled consistently with any existing modals). Shows tag counts for each side and the three action buttons.

**Logged-out behavior:** Identical to today.

---

## Security

- **Connection string:** Stored as an SWA environment variable (portal), injected server-side only. Never sent to the browser.
- **Endpoint protection:** `staticwebapp.config.json` route rule requires authentication for all `/api/*` requests. Anonymous requests receive 401 before the function runs.
- **Per-user isolation:** `userId` is read from SWA's tamper-proof server-injected header, not from the request. Users can only access their own data.
- **Cost exposure:** Even if an authenticated user abused the endpoint, Azure Table Storage costs ~$0.00036 per 10,000 operations. Impact is negligible.

---

## One-Time Setup Checklist (not implemented in code)

1. **Azure portal:** Create a Storage Account → copy connection string → add to SWA Environment Variables as `AZURE_STORAGE_CONNECTION_STRING`
2. **Google Cloud Console:** Create project → enable Google Identity → create OAuth 2.0 credentials → add authorized redirect URI `https://<your-swa-domain>/.auth/login/google/callback`
3. **Apple Developer Portal:** Create a Service ID → configure Sign In with Apple → generate client secret JWT → note client ID and secret
4. **SWA portal:** Add custom auth providers (Google, Apple) in the Authentication blade with the above credentials

---

## Future Extension: Sharing Wants

The current design is intentionally compatible with a "share your want list" feature. No data model changes are required.

### Shareable link (Option 1)

A share token is stored as two additional rows in the same `usertags` table:

```
PartitionKey: userId,         RowKey: "sharetoken", Value: "abc123xyz"   ← user's token
PartitionKey: "tok_abc123xyz", RowKey: "ref",        Value: userId        ← reverse lookup
```

A new public endpoint `GET /api/share/{token}` — **unauthenticated** — resolves the token to a userId and returns that user's `want` array (not `played`, which is more personal). The `staticwebapp.config.json` auth rule carves out `/api/share/*` from the authentication requirement. A "Copy share link" button in the logged-in header generates the token on first use and writes it to storage.

The share URL format `https://<site>/?share=<token>` requires no new SWA routing — `app.js` detects the `share=` param and renders a read-only view of that user's wants.

### Targeted sharing and group views (Options 2 & 3)

Sharing with specific people is already covered by Option 1 — the shareable link IS the targeting mechanism (you control who receives the URL). A group aggregate view would need a new endpoint that accepts multiple share tokens and unions their `want` arrays; nothing in the current data model blocks this.

### Design decision to revisit

When sharing is implemented, decide whether `played` tags should be optionally includable in a share link (e.g., `?share=abc123&include=played`), or remain permanently private.

---

## Out of Scope

- Syncing filter/sort state or theme preferences
- Real-time multi-device sync (tags sync on page load and on change; no live push)
- Admin visibility into user data
- Account deletion / data export (can be added later)
- Share link generation and the public share endpoint (see Future Extension above)
