// ─── Service worker registration ─────────────────────────────────────────────

if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js').catch(() => {});
  });
}

// ─── Sort keys ───────────────────────────────────────────────────────────────

const PMAX_CAP = 20; // matches parser cap; maxPlayers === PMAX_CAP means "any"

const SORT_KEYS = {
  n:    r => r.name.toLowerCase(),
  tags: r => { const t = TAGS[r.id] || {}; return (t.want ? 2 : 0) + (t.played ? 1 : 0); },
  pmn:  r => r.minPlayers ?? Infinity,
  pmx:  r => r.maxPlayers != null ? (r.maxPlayers >= PMAX_CAP ? PMAX_CAP + 0.5 : r.maxPlayers) : Infinity,
  ob:   r => r.bestPlayers        && r.bestPlayers.length        ? (r.bestPlayers.length        > 6 ? PMAX_CAP + 0.5 : r.bestPlayers[0])              : Infinity,
  or:   r => r.recommendedPlayers && r.recommendedPlayers.length ? (r.recommendedPlayers.length > 6 ? PMAX_CAP + 0.5 : Math.min(...r.recommendedPlayers)) : Infinity,
  w:    r => r.weight,
  tmn:  r => r.minTime ?? Infinity,
  tmx:  r => r.maxTime ?? Infinity,
  a:    r => r.avgRating,
  g:    r => r.geekRating,
  v:    r => r.votes,
  rank: r => r.bggRank ?? Infinity,
};

function getSortFn(key) {
  if (SORT_KEYS[key]) return SORT_KEYS[key];
  if (key.startsWith('sr_')) {
    const k = key.slice(3);
    return r => r.subRanks?.[k] ?? Infinity;
  }
  return () => 0;
}

// ─── State ───────────────────────────────────────────────────────────────────

const DEFAULTS = {
  q: '',
  hideUnranked: false,
  players: null,
  weight: null,
  timeMax: null,
  minRating: null,
  ratingField: 'a',
  bestOnly: false,
  tags: { played: false, want: false },
  mechanics: [],
  mechanicsMode: 'OR',
  categories: [],
  categoriesMode: 'OR',
  sortKey: 'n',
  sortDir: -1,
  extraRankCols: [],  // array of sub-rank keys e.g. ['strategy','family']
};

let DATA = [];
let MECHANICS  = [];
let CATEGORIES = [];
const state = JSON.parse(JSON.stringify(DEFAULTS));
let TAGS = {};
const EXPANDED = new Set();
let ratingExpanded = false;

// ─── DOM refs ─────────────────────────────────────────────────────────────────

const tbody        = document.getElementById('tbody');
const gamesTable   = document.getElementById('gamesTable');
const countEl      = document.getElementById('count');
const search       = document.getElementById('search');
const hideCheck    = document.getElementById('hideUnranked');
const bestCheck    = document.getElementById('bestOnly');
const ratingFieldSel = document.getElementById('ratingField');
const sortDdBtn    = document.getElementById('sortDdBtn');
const sortDdMenu   = document.getElementById('sortDdMenu');
const sortDdText   = document.getElementById('sortDdText');
const sortDirBtn   = document.getElementById('sortDirBtn');
const pickBtn      = document.getElementById('pickBtn');
const themeBtn     = document.getElementById('themeBtn');
const installBtn   = document.getElementById('installBtn');
const updatedEl    = document.getElementById('updated');

// multi-select elements
const mechanicSelect   = document.getElementById('mechanicSelect');
const mechanicInput    = document.getElementById('mechanicInput');
const mechanicDropdown = document.getElementById('mechanicDropdown');
const mechanicModeBtn  = document.getElementById('mechanicMode');

const categorySelect   = document.getElementById('categorySelect');
const categoryInput    = document.getElementById('categoryInput');
const categoryDropdown = document.getElementById('categoryDropdown');
const categoryModeBtn  = document.getElementById('categoryMode');

// ─── Helpers ─────────────────────────────────────────────────────────────────

const escapeHtml = s => String(s).replace(/[&<>"']/g, c => ({
  '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
}[c]));

function fmt(n, dec) {
  if (n === 0 || n == null) return '<span class="dim">—</span>';
  return n.toFixed(dec);
}

const SUB_RANK_LABELS = {
  abstracts: 'Abstract',
  cgs:       'Card Game',
  childrens: "Children's",
  family:    'Family',
  party:     'Party',
  strategy:  'Strategy',
  thematic:  'Thematic',
  wargames:  'Wargame',
};

function imgTooltipAttrs(r) {
  if (!r.thumbnail) return '';
  return ` data-thumbnail="${escapeHtml(r.thumbnail)}"` + (r.yearPublished ? ` data-year="${r.yearPublished}"` : '');
}

function rankTooltipAttrs(r) {
  const hasSubranks = Object.keys(r.subRanks || {}).some(k => r.subRanks[k] != null);
  if (!hasSubranks) return '';
  return ` data-rank-overall="${r.bggRank}" data-sub-ranks='${JSON.stringify(r.subRanks)}'`;
}

function formatPollList(arr) {
  if (!arr || arr.length === 0) return '<span class="dim">—</span>';
  if (arr.length > 6) return 'any';
  return arr.join(',');
}

/** "2–4" or "3" from a sorted array of player counts */
function playerRangeText(arr) {
  if (!arr || arr.length === 0) return null;
  const sorted = [...arr].sort((a, b) => a - b);
  if (sorted.length > 6) return 'any';
  const min = sorted[0], max = sorted[sorted.length - 1];
  return min === max ? String(min) : `${min}–${max}`;
}

/** "2–4p" from minPlayers / maxPlayers */
function playerCountText(r) {
  if (r.minPlayers === r.maxPlayers) return String(r.minPlayers);
  return `${r.minPlayers}–${r.maxPlayers}`;
}

/** "30–60 min" or "60 min" from minTime / maxTime */
function timeRangeText(r) {
  if (r.minTime == null && r.maxTime == null) return null;
  if (r.minTime == null) return r.maxTime + ' min';
  if (r.maxTime == null || r.minTime === r.maxTime) return r.minTime + ' min';
  return `${r.minTime}–${r.maxTime} min`;
}

function listText(arr) {
  if (!arr || arr.length === 0) return '—';
  if (arr.length > 6) return 'any';
  return arr.join(',');
}

// ─── Filter logic ─────────────────────────────────────────────────────────────

function matches(r, s) {
  if (s.q && !r.name.toLowerCase().includes(s.q)) return false;
  if (s.hideUnranked && !(r.geekRating > 0)) return false;

  if (s.players != null) {
    const n = s.players;
    if (n === 6) { if (!(r.maxPlayers >= 6)) return false; }
    else { if (!(r.minPlayers <= n && n <= r.maxPlayers)) return false; }
    if (s.bestOnly) {
      if (!Array.isArray(r.bestPlayers) || r.bestPlayers.length === 0) return false;
      if (n === 6) { if (!r.bestPlayers.some(x => x >= 6)) return false; }
      else { if (!r.bestPlayers.includes(n)) return false; }
    }
  }

  if (s.weight === 'light' && !(r.weight > 0 && r.weight < 2)) return false;
  if (s.weight === 'med'   && !(r.weight >= 2 && r.weight <= 3)) return false;
  if (s.weight === 'heavy' && !(r.weight > 3)) return false;

  if (s.timeMax != null && !(r.minTime != null && r.minTime <= s.timeMax)) return false;

  if (s.minRating != null) {
    let rating;
    if (s.ratingField === 'a') rating = r.avgRating;
    else if (s.ratingField === 'g') rating = r.geekRating;
    else rating = r.avgRating;
    if (!(rating > 0 && rating >= s.minRating)) return false;
  }

  if (s.tags.played || s.tags.want || s.tags.unplayed) {
    const t = TAGS[r.id] || {};
    if (s.tags.played   && !t.played)  return false;
    if (s.tags.want     && !t.want)    return false;
    if (s.tags.unplayed &&  t.played)  return false;
  }

  // mechanics filter
  if (s.mechanics.length > 0) {
    const gameMechanics = r.mechanics || [];
    if (s.mechanicsMode === 'AND') {
      if (!s.mechanics.every(m => gameMechanics.includes(m))) return false;
    } else {
      if (!s.mechanics.some(m => gameMechanics.includes(m))) return false;
    }
  }

  // categories filter
  if (s.categories.length > 0) {
    const gameCategories = r.categories || [];
    if (s.categoriesMode === 'AND') {
      if (!s.categories.every(c => gameCategories.includes(c))) return false;
    } else {
      if (!s.categories.some(c => gameCategories.includes(c))) return false;
    }
  }

  return true;
}

// ─── Tags (shortlist) ─────────────────────────────────────────────────────────

const TAGS_KEY = 'gpw.tags';

function loadTags() {
  try { TAGS = JSON.parse(localStorage.getItem(TAGS_KEY) || '{}') || {}; }
  catch { TAGS = {}; }
}

function saveTags() {
  try { localStorage.setItem(TAGS_KEY, JSON.stringify(TAGS)); } catch {}
}

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

// ─── MD5 (for Gravatar URLs) ──────────────────────────────────────────────────
// RFC 1321 implementation based on blueimp/JavaScript-MD5 (MIT)

function md5(str) {
  str = unescape(encodeURIComponent(str)); // normalise to UTF-8 bytes
  function add(x, y) { const l = (x & 0xFFFF) + (y & 0xFFFF); return (((x >> 16) + (y >> 16) + (l >> 16)) << 16) | (l & 0xFFFF); }
  function rol(n, s) { return (n << s) | (n >>> (32 - s)); }
  function cmn(q, a, b, x, s, t) { return add(rol(add(add(a, q), add(x, t)), s), b); }
  function ff(a,b,c,d,x,s,t) { return cmn((b & c) | (~b & d), a, b, x, s, t); }
  function gg(a,b,c,d,x,s,t) { return cmn((b & d) | (c & ~d), a, b, x, s, t); }
  function hh(a,b,c,d,x,s,t) { return cmn(b ^ c ^ d,      a, b, x, s, t); }
  function ii(a,b,c,d,x,s,t) { return cmn(c ^ (b | ~d),   a, b, x, s, t); }
  const n = str.length;
  const blks = new Array((((n + 8) >> 6) + 1) * 16).fill(0);
  for (let i = 0; i < n; i++) blks[i >> 2] |= str.charCodeAt(i) << ((i % 4) * 8);
  blks[n >> 2] |= 0x80 << ((n % 4) * 8);
  blks[blks.length - 2] = n * 8;
  let a = 1732584193, b = -271733879, c = -1732584194, d = 271733878;
  for (let i = 0; i < blks.length; i += 16) {
    const [oa, ob, oc, od] = [a, b, c, d], m = blks.slice(i, i + 16);
    a=ff(a,b,c,d,m[0], 7,-680876936);   d=ff(d,a,b,c,m[1],12,-389564586);
    c=ff(c,d,a,b,m[2],17, 606105819);   b=ff(b,c,d,a,m[3],22,-1044525330);
    a=ff(a,b,c,d,m[4], 7,-176418897);   d=ff(d,a,b,c,m[5],12, 1200080426);
    c=ff(c,d,a,b,m[6],17,-1473231341);  b=ff(b,c,d,a,m[7],22,-45705983);
    a=ff(a,b,c,d,m[8], 7, 1770035416);  d=ff(d,a,b,c,m[9],12,-1958414417);
    c=ff(c,d,a,b,m[10],17,-42063);      b=ff(b,c,d,a,m[11],22,-1990404162);
    a=ff(a,b,c,d,m[12], 7, 1804603682); d=ff(d,a,b,c,m[13],12,-40341101);
    c=ff(c,d,a,b,m[14],17,-1502002290); b=ff(b,c,d,a,m[15],22, 1236535329);
    a=gg(a,b,c,d,m[1], 5,-165796510);   d=gg(d,a,b,c,m[6], 9,-1069501632);
    c=gg(c,d,a,b,m[11],14, 643717713);  b=gg(b,c,d,a,m[0],20,-373897302);
    a=gg(a,b,c,d,m[5], 5,-701558691);   d=gg(d,a,b,c,m[10], 9, 38016083);
    c=gg(c,d,a,b,m[15],14,-660478335);  b=gg(b,c,d,a,m[4],20,-405537848);
    a=gg(a,b,c,d,m[9], 5, 568446438);   d=gg(d,a,b,c,m[14], 9,-1019803690);
    c=gg(c,d,a,b,m[3],14,-187363961);   b=gg(b,c,d,a,m[8],20, 1163531501);
    a=gg(a,b,c,d,m[13], 5,-1444681467); d=gg(d,a,b,c,m[2], 9,-51403784);
    c=gg(c,d,a,b,m[7],14, 1735328473);  b=gg(b,c,d,a,m[12],20,-1926607734);
    a=hh(a,b,c,d,m[5], 4,-378558);      d=hh(d,a,b,c,m[8],11,-2022574463);
    c=hh(c,d,a,b,m[11],16, 1839030562); b=hh(b,c,d,a,m[14],23,-35309556);
    a=hh(a,b,c,d,m[1], 4,-1530992060);  d=hh(d,a,b,c,m[4],11, 1272893353);
    c=hh(c,d,a,b,m[7],16,-155497632);   b=hh(b,c,d,a,m[10],23,-1094730640);
    a=hh(a,b,c,d,m[13], 4, 681279174);  d=hh(d,a,b,c,m[0],11,-358537222);
    c=hh(c,d,a,b,m[3],16,-722521979);   b=hh(b,c,d,a,m[6],23, 76029189);
    a=hh(a,b,c,d,m[9], 4,-640364487);   d=hh(d,a,b,c,m[12],11,-421815835);
    c=hh(c,d,a,b,m[15],16, 530742520);  b=hh(b,c,d,a,m[2],23,-995338651);
    a=ii(a,b,c,d,m[0], 6,-198630844);   d=ii(d,a,b,c,m[7],10, 1126891415);
    c=ii(c,d,a,b,m[14],15,-1416354905); b=ii(b,c,d,a,m[5],21,-57434055);
    a=ii(a,b,c,d,m[12], 6, 1700485571); d=ii(d,a,b,c,m[3],10,-1894986606);
    c=ii(c,d,a,b,m[10],15,-1051523);    b=ii(b,c,d,a,m[1],21,-2054922799);
    a=ii(a,b,c,d,m[8], 6, 1873313359);  d=ii(d,a,b,c,m[15],10,-30611744);
    c=ii(c,d,a,b,m[6],15,-1560198380);  b=ii(b,c,d,a,m[13],21, 1309151649);
    a=ii(a,b,c,d,m[4], 6,-145523070);   d=ii(d,a,b,c,m[11],10,-1120210379);
    c=ii(c,d,a,b,m[2],15, 718787259);   b=ii(b,c,d,a,m[9],21,-343485551);
    a=add(a,oa); b=add(b,ob); c=add(c,oc); d=add(d,od);
  }
  return [a, b, c, d]
    .map(n => [0,1,2,3].map(j => ((n >> (j*8)) & 0xff).toString(16).padStart(2,'0')).join(''))
    .join('');
}

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
    if (!Number.isFinite(n) || n <= 0) continue;
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
    const ta = a[k], tb = b[k];
    if (!ta || !tb || typeof ta !== 'object' || typeof tb !== 'object') return false;
    return !!ta.want === !!tb.want && !!ta.played === !!tb.played;
  });
}

async function checkAuth() {
  try {
    const res = await fetch('/.auth/me');
    if (!res.ok) { AUTH_USER = null; return null; }
    const data = await res.json();
    AUTH_USER = data.clientPrincipal || null;
    return AUTH_USER;
  } catch {
    AUTH_USER = null;
    return null;
  }
}

function updateAuthUI(user) {
  const signInEl   = document.getElementById('authSignIn');
  const userEl     = document.getElementById('authUser');
  const avatarEl   = document.getElementById('authAvatar');
  const menuAvatar = document.getElementById('authMenuAvatar');
  const menuName   = document.getElementById('authMenuName');
  if (!signInEl || !userEl || !avatarEl) return;

  if (!user) {
    signInEl.hidden = false;
    userEl.hidden   = true;
    themeBtn.hidden = false;
    return;
  }
  themeBtn.hidden = true;

  const display = user.userDetails || user.userId || '';
  const picture = user.claims?.find(c => c.typ === 'picture')?.val;
  const gravatarSrc = display
    ? `https://www.gravatar.com/avatar/${md5(display.toLowerCase().trim())}?d=identicon&s=48`
    : null;
  const src = picture || gravatarSrc;

  for (const el of [avatarEl, menuAvatar].filter(Boolean)) {
    el.textContent = '';
    if (src) {
      const img = document.createElement('img');
      img.src = src;
      img.alt = '';
      img.className = 'auth-avatar-img';
      el.appendChild(img);
    } else {
      el.textContent = display ? display[0].toUpperCase() : '?';
    }
  }
  if (menuName) menuName.textContent = display;

  signInEl.hidden = true;
  userEl.hidden   = false;
}

function setSyncStatus(status) {
  SYNC_STATUS = status;
  const el = document.getElementById('syncStatus');
  if (!el) return;
  el.dataset.status = status;
  el.textContent = status === 'syncing' ? '↻ Syncing…'
                 : status === 'synced'  ? '☁ Synced'
                 : status === 'offline' ? '⚠ Offline'
                 : '';
  el.hidden = (status === 'idle' || status === 'synced');
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
      render();
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

function showConflictModal(localTags, cloudTags) {
  return new Promise(resolve => {
    const modal    = document.getElementById('conflictModal');
    const desc     = document.getElementById('conflictDesc');
    const btnCloud = document.getElementById('conflictCloud');
    const btnLocal = document.getElementById('conflictLocal');
    const btnMerge = document.getElementById('conflictMerge');

    const localCount = Object.keys(localTags).length;
    const cloudCount = Object.keys(cloudTags).length;
    desc.textContent =
      `This device has tags for ${localCount} game${localCount !== 1 ? 's' : ''}. ` +
      `Your account has tags for ${cloudCount} game${cloudCount !== 1 ? 's' : ''}. ` +
      `What should we do?`;

    let settled = false;

    function finish(result) {
      if (settled) return;
      settled = true;
      modal.close();
      cleanup();
      resolve(result);
    }

    function cleanup() {
      btnCloud.removeEventListener('click', onCloud);
      btnLocal.removeEventListener('click', onLocal);
      btnMerge.removeEventListener('click', onMerge);
      modal.removeEventListener('cancel', onCancel);
    }

    const onCloud  = () => finish(cloudTags);
    const onLocal  = () => finish(localTags);
    const onMerge  = () => finish(mergeTags(localTags, cloudTags));
    // Escape key / backdrop: treat as "merge" (safe default, matches previous stub behavior)
    const onCancel = e => { e.preventDefault(); finish(mergeTags(localTags, cloudTags)); };

    btnCloud.addEventListener('click', onCloud);
    btnLocal.addEventListener('click', onLocal);
    btnMerge.addEventListener('click', onMerge);
    modal.addEventListener('cancel', onCancel);

    modal.showModal();
  });
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
      const prevTags = TAGS;
      TAGS = cloudRuntime;
      try { saveTags(); } catch { TAGS = prevTags; setSyncStatus('offline'); return false; }
      setSyncStatus('synced');
      return false;
    }
    if (cloudEmpty) {
      await syncToCloud(); return false;
    }
    if (tagsAreEqual(TAGS, cloudRuntime)) {
      setSyncStatus('synced'); return false;
    }

    // Both sides non-empty and diverged — show conflict modal (wired in Task 8)
    const resolved = await showConflictModal(TAGS, cloudRuntime);
    const prevTags2 = TAGS;
    TAGS = resolved;
    try { saveTags(); } catch { TAGS = prevTags2; setSyncStatus('offline'); return false; }
    await syncToCloud();
    return true;
  } catch { setSyncStatus('offline'); return false; }
}

// ─── URL / localStorage state ─────────────────────────────────────────────────

const STATE_KEY = 'gpw.state';

function serializeState(s) {
  const p = new URLSearchParams();
  if (s.q) p.set('q', s.q);
  if (s.hideUnranked) p.set('unranked', '1');
  if (s.players != null) p.set('players', String(s.players));
  if (s.weight) p.set('weight', s.weight);
  if (s.timeMax != null) p.set('time', String(s.timeMax));
  if (s.minRating != null) p.set('rating', String(s.minRating));
  if (s.ratingField !== DEFAULTS.ratingField) p.set('ratingField', s.ratingField);
  if (s.bestOnly) p.set('best', '1');
  if (s.sortKey !== DEFAULTS.sortKey || s.sortDir !== DEFAULTS.sortDir) {
    p.set('sort', s.sortKey);
    p.set('dir', s.sortDir === 1 ? 'asc' : 'desc');
  }
  // new: mechanics & categories
  if (s.mechanics.length > 0) p.set('mechanics', s.mechanics.join('|'));
  if (s.mechanicsMode !== 'OR') p.set('mechanicsMode', s.mechanicsMode);
  if (s.categories.length > 0) p.set('categories', s.categories.join('|'));
  if (s.categoriesMode !== 'OR') p.set('categoriesMode', s.categoriesMode);
  if (s.extraRankCols.length > 0) p.set('xrank', s.extraRankCols.join('|'));
  return p;
}

function parseState(params) {
  const out = {};
  if (params.has('q')) out.q = params.get('q');
  if (params.has('unranked')) out.hideUnranked = true;
  if (params.has('players')) {
    const n = parseInt(params.get('players'), 10);
    if (n >= 1 && n <= 6) out.players = n;
  }
  const w = params.get('weight');
  if (w === 'light' || w === 'med' || w === 'heavy') out.weight = w;
  if (params.has('time')) {
    const t = parseInt(params.get('time'), 10);
    if ([30, 60, 90, 120].includes(t)) out.timeMax = t;
  }
  if (params.has('rating')) {
    const v = parseFloat(params.get('rating'));
    if ([6, 6.5, 7, 7.5, 8].includes(v)) out.minRating = v;
  }
  if (params.has('ratingField')) {
    const v = params.get('ratingField');
    if (v === 'a' || v === 'g') out.ratingField = v;
  }
  if (params.get('best') === '1') out.bestOnly = true;
  if (params.has('sort')) {
    const k = params.get('sort');
    if (SORT_KEYS[k] || k.startsWith('sr_')) {
      out.sortKey = k;
      const d = params.get('dir');
      out.sortDir = d === 'asc' ? 1 : (d === 'desc' ? -1 : pickSortDir(k));
    }
  }
  if (params.has('mechanics')) {
    const raw = params.get('mechanics').split('|').map(s => s.trim()).filter(Boolean);
    if (raw.length) out.mechanics = raw;
  }
  const mm = params.get('mechanicsMode');
  if (mm === 'AND' || mm === 'OR') out.mechanicsMode = mm;
  if (params.has('categories')) {
    const raw = params.get('categories').split('|').map(s => s.trim()).filter(Boolean);
    if (raw.length) out.categories = raw;
  }
  const cm = params.get('categoriesMode');
  if (cm === 'AND' || cm === 'OR') out.categoriesMode = cm;
  if (params.has('xrank')) {
    const valid = Object.keys(SUB_RANK_LABELS);
    const raw = params.get('xrank').split('|').filter(k => valid.includes(k));
    if (raw.length) out.extraRankCols = raw;
  }
  return out;
}

function persistState() {
  const qs = serializeState(state).toString();
  const url = qs ? '?' + qs : location.pathname;
  history.replaceState(null, '', url);
  try { localStorage.setItem(STATE_KEY, qs); } catch {}
}

function hydrateState() {
  const urlParams = new URLSearchParams(location.search);
  if ([...urlParams].length > 0) {
    Object.assign(state, parseState(urlParams));
    return;
  }
  try {
    const saved = localStorage.getItem(STATE_KEY);
    if (saved) {
      Object.assign(state, parseState(new URLSearchParams(saved)));
      persistState();
    }
  } catch {}
}

function update(partial) {
  Object.assign(state, partial);
  persistState();
  render();
}

// ─── Render ───────────────────────────────────────────────────────────────────


function filteredRows() {
  const s = { ...state, q: state.q.toLowerCase() };
  const rows = DATA.filter(r => matches(r, s));
  const k = getSortFn(state.sortKey);
  const dir = state.sortDir;
  rows.sort((a, b) => {
    const av = k(a), bv = k(b);
    if (av === Infinity && bv === Infinity) return 0;
    if (av === Infinity) return 1;
    if (bv === Infinity) return -1;
    if (av < bv) return -dir;
    if (av > bv) return dir;
    return 0;
  });
  return rows;
}

const SORT_FIELD = {
  pmn:  r => ['Players', playerCountText(r)],
  pmx:  r => ['Players', playerCountText(r)],
  ob:   r => ['Best', playerRangeText(r.bestPlayers) ?? '—'],
  or:   r => ['Rec', playerRangeText(r.recommendedPlayers) ?? '—'],
  w:    r => r.weight > 0 ? ['Weight', r.weight.toFixed(2)] : null,
  tmn:  r => timeRangeText(r) ? ['Time', timeRangeText(r)] : null,
  tmx:  r => timeRangeText(r) ? ['Time', timeRangeText(r)] : null,
  a:    r => ['Avg', r.avgRating > 0 ? r.avgRating.toFixed(2) : '—'],
  g:    r => ['Geek', r.geekRating > 0 ? r.geekRating.toFixed(2) : '—'],
  v:    r => ['Votes', r.votes.toLocaleString()],
  rank: r => ['BGG Rank', r.bggRank != null ? '#' + r.bggRank : '—'],
  tags: r => {
    const t = TAGS[r.id] || {};
    const marks = (t.want ? '♥ ' : '') + (t.played ? '✓' : '');
    return ['Shortlist', marks.trim() || '—'];
  },
};

function sortFieldPart(r, sk) {
  const fn = SORT_FIELD[sk];
  if (!fn) return '';
  const result = fn(r);
  if (!result) return '';
  const [label, value] = result;
  return `<span class="rating-part sort-active"><span class="rating-label">${label}</span> ${escapeHtml(String(value))}</span>`;
}

function buildCardDetail(r) {
  const bestText    = playerRangeText(r.bestPlayers);
  const recText     = playerRangeText(r.recommendedPlayers);
  const time        = timeRangeText(r) ?? '—';

  const hasSubranks = r.subRanks && Object.values(r.subRanks).some(v => v != null);
  const subranksHtml = hasSubranks
    ? '<div class="cd-section">'
        + '<span class="cd-label">Category Ranks</span>'
        + '<div class="cd-chips">'
        + Object.entries(SUB_RANK_LABELS)
            .filter(([k]) => r.subRanks?.[k] != null)
            .map(([k, label]) => `<span class="cd-chip"><span class="cd-chip-cat">${escapeHtml(label)}</span><span class="cd-chip-val">#${r.subRanks[k]}</span></span>`)
            .join('')
        + '</div></div>'
    : '';

  const mechanicsHtml = r.mechanics && r.mechanics.length
    ? '<div class="cd-section">'
        + '<span class="cd-label">Mechanics</span>'
        + '<div class="cd-chips">'
        + r.mechanics.map(m => `<span class="cd-chip">${escapeHtml(m)}</span>`).join('')
        + '</div></div>'
    : '';

  const categoriesHtml = r.categories && r.categories.length
    ? '<div class="cd-section">'
        + '<span class="cd-label">Categories</span>'
        + '<div class="cd-chips">'
        + r.categories.map(c => `<span class="cd-chip">${escapeHtml(c)}</span>`).join('')
        + '</div></div>'
    : '';

  return '<div class="cd-inner"><div class="cd-body">'
    + '<div class="cd-bottom">'
    + '<div class="cd-trio">'
    +   (recText  ? `<div class="cd-item"><span class="cd-label">Rec. Players</span><span class="cd-val">${escapeHtml(recText)}</span></div>` : '<div class="cd-item"></div>')
    +   (bestText ? `<div class="cd-item"><span class="cd-label">Best At</span><span class="cd-val">${escapeHtml(bestText)}</span></div>` : '<div class="cd-item"></div>')
    +   `<div class="cd-item"><span class="cd-label">Time</span><span class="cd-val">${escapeHtml(time)}</span></div>`
    + '</div>'
    + subranksHtml
    + mechanicsHtml
    + categoriesHtml
    + '</div>'
    + `<a class="cd-bgg-link" href="https://boardgamegeek.com/boardgame/${r.id}" target="_blank" rel="noopener">View on BoardGameGeek ↗</a>`
    + '</div></div>';
}

function render() {
  const rows = filteredRows();
  const countText = rows.length === DATA.length
    ? rows.length + ' games'
    : rows.length + ' of ' + DATA.length;
  countEl.textContent = countText;

  tbody.innerHTML = rows.map(r => {
    const t = TAGS[r.id] || {};
    const tagBtns = '<span class="tag-btns">'
      + `<button type="button" class="tag-btn${t.want   ? ' active' : ''}" data-id="${r.id}" data-tag="want"   aria-label="Wishlist" title="Wishlist">${t.want   ? '♥' : '♡'}</button>`
      + `<button type="button" class="tag-btn${t.played ? ' active' : ''}" data-id="${r.id}" data-tag="played" aria-label="Played"   title="Played">✓</button>`
      + '</span>';
    const sk = state.sortKey;
    const cls = key => key === sk ? ' sort-active' : '';
    const playerRange = playerCountText(r);
    const miniRank   = r.bggRank != null ? '#' + r.bggRank : '—';
    const miniRating = r.avgRating > 0 ? fmt(r.avgRating, 2) : '—';
    const miniWeight = r.weight > 0 ? fmt(r.weight, 2) : '—';
    const miniCover  = r.thumbnail
      ? `<img class="card-cover-img" src="${escapeHtml(r.thumbnail)}" alt="" loading="lazy">`
      : '<div class="card-cover-ph"></div>';
    const miniItems = [
      { label: 'Rank',    val: miniRank,               keys: ['rank'] },
      { label: 'Players', val: escapeHtml(playerRange), keys: ['pmn'] },
      { label: 'Rating',  val: miniRating,              keys: ['a'] },
      { label: 'Weight',  val: miniWeight,              keys: ['w'] },
    ];
    const sortPart = sortFieldPart(r, sk);
    const miniHasSortKey = miniItems.some(item => item.keys.includes(sk));
    const cardMini = '<div class="card-mini">'
      + miniCover
      + '<div class="card-mini-stats">'
      + miniItems.map(item => {
          const active = item.keys.includes(sk) ? ' sort-active' : '';
          return `<div class="cms-item${active}"><span class="cms-label">${item.label}</span><span class="cms-val">${item.val}</span></div>`;
        }).join('')
      + '</div></div>'
      + ((!miniHasSortKey && sortPart) ? `<div class="card-sort-extra">${sortPart}</div>` : '');

    const best    = playerRangeText(r.bestPlayers) ?? '<span class="dim">—</span>';
    const rec     = playerRangeText(r.recommendedPlayers) ?? '<span class="dim">—</span>';
    const time    = timeRangeText(r) ?? '<span class="dim">—</span>';
    const hasSubranks = r.subRanks && Object.values(r.subRanks).some(v => v != null);
    const rankCell = r.bggRank != null
      ? `<span class="${r.bggRank <= 10 ? 'top-ten' : ''}${hasSubranks ? ' has-subranks' : ''}">#${r.bggRank}</span>`
      : '<span class="dim">—</span>';

    return '<tr data-id="' + r.id + '"' + (EXPANDED.has(r.id) ? ' class="expanded"' : '') + (sk === 'n' ? ' data-sort-name' : '') + '>'
      + '<td class="name" data-label="Game"' + imgTooltipAttrs(r) + '><div class="card-header"><div class="card-name-body"><a href="https://boardgamegeek.com/boardgame/' + r.id + '" target="_blank" rel="noopener">' + escapeHtml(r.name) + '</a></div><div class="card-controls"><span class="card-tags-mobile">' + tagBtns + '</span><span class="card-chevron" aria-hidden="true"></span></div></div>' + cardMini + '</td>'
      + '<td class="ctr tags-cell" data-label="My list">' + tagBtns + '</td>'
      + `<td class="ctr${cls('ob')}" data-label="Best">${best}</td>`
      + `<td class="ctr${cls('pmn')}" data-label="Players">${playerRange}</td>`
      + `<td class="ctr${cls('or')}" data-label="Rec">${rec}</td>`
      + `<td class="num${cls('w')}" data-label="Weight">${fmt(r.weight, 2)}</td>`
      + `<td class="num${cls('tmn')}" data-label="Time">${time}</td>`
      + `<td class="ctr rating-group-start${cls('a')}" data-label="Avg">${fmt(r.avgRating, 2)}</td>`
      + `<td class="num rating-detail${cls('g')}" data-label="Geek">${fmt(r.geekRating, 2)}</td>`
      + `<td class="num rating-detail rating-group-end${cls('v')}" data-label="Votes">${r.votes.toLocaleString()}</td>`
      + `<td class="num rank-col${cls('rank')}" data-label="Rank"${rankTooltipAttrs(r)}>${rankCell}</td>`
      + `<td class="card-detail">${buildCardDetail(r)}</td>`
      + state.extraRankCols.map(k => {
          const val = r.subRanks?.[k];
          const sk = 'sr_' + k;
          return `<td class="num extra-rank-td${cls(sk)}" data-label="${SUB_RANK_LABELS[k]}">${val != null ? '#' + val : '<span class="dim">—</span>'}</td>`;
        }).join('')
      + '</tr>';
  }).join('');

  syncControls();
}

function syncControls() {
  document.querySelectorAll('thead th').forEach(th => {
    const arrow = th.querySelector('.arrow');
    if (th.dataset.k === state.sortKey) {
      th.classList.add('active');
      if (arrow) arrow.textContent = state.sortDir === 1 ? '▲' : '▼';
    } else {
      th.classList.remove('active');
      if (arrow) arrow.textContent = '▲';
    }
  });

  document.querySelectorAll('.chip-group').forEach(group => {
    const name = group.dataset.name;
    if (name === 'mechanics' || name === 'categories') return; // handled separately
    group.querySelectorAll('.chip').forEach(c => {
      if (name === 'tags') {
        c.classList.toggle('active', !!state.tags[c.dataset.tag]);
        return;
      }
      const raw = c.dataset.v;
      const v = (name === 'players' || name === 'timeMax') ? parseInt(raw, 10)
        : (name === 'minRating') ? parseFloat(raw)
        : raw;
      c.classList.toggle('active', state[name] === v);
    });
  });

  if (document.activeElement !== search) search.value = state.q;
  hideCheck.checked  = state.hideUnranked;
  bestCheck.checked  = state.bestOnly;
  bestCheck.disabled = state.players == null;
  ratingFieldSel.value = state.ratingField;

  // Sync mobile sort dropdown — add/remove sr_* entries to match extraRankCols
  sortDdMenu.querySelectorAll('li.extra-rank-sort').forEach(el => el.remove());
  state.extraRankCols.forEach(k => {
    const li = document.createElement('li');
    li.className = 'extra-rank-sort';
    li.dataset.k = 'sr_' + k;
    li.textContent = SUB_RANK_LABELS[k] + ' Rank';
    sortDdMenu.appendChild(li);
  });
  sortDdMenu.querySelectorAll('li').forEach(li => {
    li.classList.toggle('selected', li.dataset.k === state.sortKey);
  });
  const selectedLi = sortDdMenu.querySelector('li.selected');
  if (selectedLi) sortDdText.textContent = selectedLi.textContent;
  sortDirBtn.textContent = state.sortDir === 1 ? '▲' : '▼';

  // sync mechanic/category tags and mode buttons
  syncMultiSelect(mechanicSelect, mechanicInput, mechanicModeBtn, 'mechanics', 'mechanicsMode');
  syncMultiSelect(categorySelect, categoryInput, categoryModeBtn, 'categories', 'categoriesMode');

  // Update active filter badge on the panel summary
  const activeCount = [
    state.players != null,
    state.weight != null,
    state.timeMax != null,
    state.minRating != null,
    state.mechanics.length > 0,
    state.categories.length > 0,
    Object.values(state.tags).some(Boolean),
    state.hideUnranked,
  ].filter(Boolean).length;
  const badge = document.getElementById('filterBadge');
  const resetBtn = document.getElementById('filterResetBtn');
  if (badge) {
    badge.hidden = activeCount === 0;
    badge.textContent = activeCount;
  }
  if (resetBtn) resetBtn.hidden = activeCount === 0;

}

// ─── Multi-select widget ──────────────────────────────────────────────────────

/**
 * Re-renders the selected tags inside the tag-input container.
 * Leaves the <input> in place (always last child).
 */
function syncMultiSelect(container, input, modeBtn, stateKey, modeKey) {
  // remove existing tags (everything except the input and dropdown)
  container.querySelectorAll('.multi-tag').forEach(el => el.remove());

  // re-insert tags before the input
  state[stateKey].forEach(value => {
    const tag = document.createElement('span');
    tag.className = 'multi-tag';
    tag.dataset.value = value;
    tag.innerHTML = `<span class="multi-tag-label">${escapeHtml(value)}</span>`
      + `<button type="button" class="multi-tag-x" aria-label="Remove ${escapeHtml(value)}">×</button>`;
    container.insertBefore(tag, input);
  });

  modeBtn.textContent = state[modeKey];
  modeBtn.classList.toggle('active', state[stateKey].length > 1);
}

/**
 * Renders dropdown options for a multi-select widget.
 * Shows items matching the query, capped at 40.
 */
function renderDropdown(dropdown, allItems, selectedItems, query) {
  const q = query.toLowerCase();
  const visible = allItems
    .filter(item => !q || item.toLowerCase().includes(q))
    .slice(0, 40);

  if (visible.length === 0) {
    dropdown.innerHTML = '<div class="multi-dropdown-empty">No matches</div>';
    return;
  }

  dropdown.innerHTML = visible.map(item => {
    const sel = selectedItems.includes(item);
    return `<div class="multi-dropdown-item${sel ? ' selected' : ''}" role="option" aria-selected="${sel}" data-value="${escapeHtml(item)}">`
      + `<span class="item-check">✓</span>`
      + `<span>${escapeHtml(item)}</span>`
      + `</div>`;
  }).join('');
}

/**
 * Wires up a multi-select tag input.
 */
function initMultiSelect({ container, input, dropdown, modeBtn, stateKey, modeKey, allItemsFn }) {
  // open dropdown on input focus / typing
  input.addEventListener('focus', () => {
    renderDropdown(dropdown, allItemsFn(), state[stateKey], input.value);
    dropdown.hidden = false;
  });

  input.addEventListener('input', () => {
    renderDropdown(dropdown, allItemsFn(), state[stateKey], input.value);
    dropdown.hidden = false;
  });

  // click on a tag's × dismiss button
  container.addEventListener('click', e => {
    const x = e.target.closest('.multi-tag-x');
    if (x) {
      const value = x.closest('.multi-tag').dataset.value;
      update({ [stateKey]: state[stateKey].filter(v => v !== value) });
      input.focus();
      return;
    }
    // click on the container itself (not input) → focus input
    if (e.target === container) input.focus();
  });

  // click on a dropdown item → toggle selection
  dropdown.addEventListener('mousedown', e => {
    // mousedown so we can prevent blur before click fires
    e.preventDefault();
    const item = e.target.closest('.multi-dropdown-item');
    if (!item) return;
    const value = item.dataset.value;
    const current = state[stateKey];
    const next = current.includes(value)
      ? current.filter(v => v !== value)
      : [...current, value];
    update({ [stateKey]: next });
    input.value = '';
    renderDropdown(dropdown, allItemsFn(), next, '');
    input.focus();
  });

  // close dropdown when focus leaves the container
  container.addEventListener('focusout', e => {
    if (!container.contains(e.relatedTarget)) {
      dropdown.hidden = true;
      input.value = '';
    }
  });

  // OR / AND mode toggle
  modeBtn.addEventListener('click', () => {
    update({ [modeKey]: state[modeKey] === 'OR' ? 'AND' : 'OR' });
  });

  // keyboard: Backspace removes last tag when input is empty
  input.addEventListener('keydown', e => {
    if (e.key === 'Backspace' && input.value === '' && state[stateKey].length > 0) {
      const next = state[stateKey].slice(0, -1);
      update({ [stateKey]: next });
      renderDropdown(dropdown, allItemsFn(), next, '');
    }
    if (e.key === 'Escape') {
      dropdown.hidden = true;
      input.value = '';
      input.blur();
    }
  });
}

// ─── Dynamic rank column headers ─────────────────────────────────────────────

const rankTh       = document.getElementById('rankTh');
const rankColPicker = document.getElementById('rankColPicker');

// Which sub-rank categories have at least one game with data (computed after load)
let availableSubranks = [];

function renderExtraColHeaders() {
  document.querySelectorAll('.extra-rank-th').forEach(el => el.remove());
  // Build in correct order by inserting sequentially with a moving reference
  let ref = rankTh;
  state.extraRankCols.forEach(k => {
    const th = document.createElement('th');
    th.className = 'num extra-rank-th';
    th.dataset.k = 'sr_' + k;
    th.innerHTML = `${SUB_RANK_LABELS[k]} <span class="arrow">▲</span>`
      + `<button type="button" class="remove-rank-col-btn" data-k="${k}" title="Remove column" aria-label="Remove ${SUB_RANK_LABELS[k]} column">×</button>`;
    ref.after(th);
    ref = th;
  });
}

function updateRankColPicker() {
  const added = new Set(state.extraRankCols);
  const available = availableSubranks.filter(k => !added.has(k));
  rankColPicker.innerHTML = available.length === 0
    ? '<li class="picker-empty">All categories added</li>'
    : available.map(k => `<li role="option" data-k="${k}">${SUB_RANK_LABELS[k]}</li>`).join('');
}

// ─── Existing event wiring ────────────────────────────────────────────────────

function pickSortDir(k) {
  return (k === 'n' || k === 'o' || k === 'rank' || k.startsWith('sr_')) ? 1 : -1;
}

// Thead sort — event delegation handles static + dynamic ths
document.querySelector('thead').addEventListener('click', e => {
  // Don't sort when clicking the expand, remove, or picker buttons
  if (e.target.closest('.rating-expand-btn, .remove-rank-col-btn, #addRankColBtn, #rankColPicker')) return;
  const th = e.target.closest('th[data-k]');
  if (!th) return;
  const k = th.dataset.k;
  if (k === state.sortKey) update({ sortDir: state.sortDir * -1 });
  else update({ sortKey: k, sortDir: pickSortDir(k) });
});

// Remove extra rank col
document.querySelector('thead').addEventListener('click', e => {
  const btn = e.target.closest('.remove-rank-col-btn');
  if (!btn) return;
  e.stopPropagation();
  const k = btn.dataset.k;
  const next = state.extraRankCols.filter(c => c !== k);
  if (state.sortKey === 'sr_' + k) update({ extraRankCols: next, sortKey: 'rank', sortDir: 1 });
  else update({ extraRankCols: next });
  renderExtraColHeaders();
});

// Add rank col picker toggle
const addRankColBtn = document.getElementById('addRankColBtn');
addRankColBtn.addEventListener('click', e => {
  e.stopPropagation();
  updateRankColPicker();
  rankColPicker.hidden = !rankColPicker.hidden;
});

rankColPicker.addEventListener('click', e => {
  const li = e.target.closest('li[data-k]');
  if (!li) return;
  const k = li.dataset.k;
  if (!state.extraRankCols.includes(k)) {
    update({ extraRankCols: [...state.extraRankCols, k] });
    renderExtraColHeaders();
  }
  rankColPicker.hidden = true;
});

document.addEventListener('click', e => {
  if (!rankColPicker.hidden && !e.target.closest('#rankTh')) {
    rankColPicker.hidden = true;
  }
});

search.addEventListener('input', e => update({ q: e.target.value }));
hideCheck.addEventListener('change', e => update({ hideUnranked: e.target.checked }));
bestCheck.addEventListener('change', e => update({ bestOnly: e.target.checked }));
ratingFieldSel.addEventListener('change', e => update({ ratingField: e.target.value }));

sortDdBtn.addEventListener('click', e => {
  e.stopPropagation();
  const open = sortDdMenu.hidden;
  sortDdMenu.hidden = !open;
  sortDdBtn.setAttribute('aria-expanded', String(open));
});

sortDdMenu.addEventListener('click', e => {
  const li = e.target.closest('li[data-k]');
  if (!li) return;
  const k = li.dataset.k;
  if (k !== state.sortKey) update({ sortKey: k, sortDir: pickSortDir(k) });
  sortDdMenu.hidden = true;
  sortDdBtn.setAttribute('aria-expanded', 'false');
});

document.addEventListener('click', e => {
  if (sortDdMenu.hidden) return;
  if (e.target.closest('#sortDd')) return;
  sortDdMenu.hidden = true;
  sortDdBtn.setAttribute('aria-expanded', 'false');
});

sortDirBtn.addEventListener('click', () => update({ sortDir: state.sortDir * -1 }));

// Rating expand/collapse
const ratingExpandBtn = document.getElementById('ratingExpandBtn');
if (ratingExpandBtn) {
  ratingExpandBtn.addEventListener('click', e => {
    e.stopPropagation(); // don't trigger column sort
    const expanded = gamesTable.classList.toggle('ratings-expanded');
    ratingExpandBtn.textContent = expanded ? '−' : '+';
  });
}


document.querySelectorAll('.chip-group').forEach(group => {
  const name = group.dataset.name;
  if (name === 'mechanics' || name === 'categories') return; // handled by initMultiSelect
  group.addEventListener('click', e => {
    const btn = e.target.closest('.chip');
    if (!btn) return;
    if (name === 'tags') {
      const tag = btn.dataset.tag;
      const next = !state.tags[tag];
      const newTags = { ...state.tags, [tag]: next };
      if (next && tag === 'played')   newTags.unplayed = false;
      if (next && tag === 'unplayed') newTags.played   = false;
      update({ tags: newTags });
      return;
    }
    const raw = btn.dataset.v;
    const parsed = (name === 'players' || name === 'timeMax') ? parseInt(raw, 10)
      : (name === 'minRating') ? parseFloat(raw)
      : raw;
    const next = state[name] === parsed ? null : parsed;
    const partial = { [name]: next };
    if (name === 'players' && next == null) partial.bestOnly = false;
    update(partial);
  });
});

let _lastTagKey = null, _lastTagTime = 0;

tbody.addEventListener('click', e => {
  const btn = e.target.closest('.tag-btn');
  if (btn) {
    e.preventDefault();
    e.stopPropagation();
    const id = parseInt(btn.dataset.id, 10);
    const tag = btn.dataset.tag;
    // Debounce: ignore repeat clicks on the same button within 400ms
    const key = id + ':' + tag;
    const now = Date.now();
    if (key === _lastTagKey && now - _lastTagTime < 400) return;
    _lastTagKey = key;
    _lastTagTime = now;
    const on = !(TAGS[id] && TAGS[id][tag]);
    setTag(id, tag, on);
    // Surgical update: patch all tag buttons for this row without re-rendering
    // (full render causes ghost clicks on mobile from layout reflow)
    tbody.querySelectorAll(`.tag-btn[data-id="${id}"][data-tag="${tag}"]`).forEach(b => {
      b.classList.toggle('active', on);
      if (tag === 'want') b.textContent = on ? '♥' : '♡';
    });
    // If a tag filter is active and this change makes the row no longer match,
    // animate the row out then re-render.
    const willDisappear =
      (state.tags.unplayed && tag === 'played'  &&  on) ||
      (state.tags.played   && tag === 'played'  && !on) ||
      (state.tags.want     && tag === 'want'    && !on);
    if (willDisappear) {
      const rowEl = tbody.querySelector(`tr[data-id="${id}"]`);
      if (rowEl) {
        rowEl.classList.add('row-exit');
        setTimeout(() => render(), 280);
      } else {
        render();
      }
    } else if (state.tags.want || state.tags.played || state.tags.unplayed) {
      render();
    }
    return;
  }
  if (e.target.closest('a')) return;
  if (!window.matchMedia('(max-width: 700px)').matches) return;
  const tr = e.target.closest('tr');
  if (!tr || tr.querySelector('td.loading')) return;
  const id = parseInt(tr.dataset.id, 10);
  if (EXPANDED.has(id)) {
    EXPANDED.delete(id);
    tr.classList.remove('expanded');
    const rect = tr.getBoundingClientRect();
    if (rect.top < 0) {
      window.scrollTo({ top: window.scrollY + rect.top - 8, behavior: 'smooth' });
    }
  } else {
    EXPANDED.add(id);
    tr.classList.add('expanded');
  }
});

// ─── Cover art tooltip (fixed-position to escape table overflow clipping) ────

const imgTt     = document.createElement('div');
imgTt.id        = 'img-tooltip';
const imgTtImg  = document.createElement('img');
imgTtImg.width  = 200;
imgTtImg.loading = 'lazy';
const imgTtYear = document.createElement('span');
imgTtYear.className = 'tt-year';
imgTt.append(imgTtImg, imgTtYear);
document.body.appendChild(imgTt);

tbody.addEventListener('mouseover', e => {
  if (window.matchMedia('(max-width: 700px)').matches) return;
  const cell = e.target.closest('td.name');
  if (!cell || !cell.dataset.thumbnail) { imgTt.style.display = 'none'; return; }
  imgTtImg.src         = cell.dataset.thumbnail;
  imgTtImg.alt         = cell.textContent.trim();
  imgTtYear.textContent = cell.dataset.year || '';
  imgTtYear.hidden      = !cell.dataset.year;
  imgTt.style.display   = 'flex';
  const rect = cell.getBoundingClientRect();
  imgTt.style.left = (rect.right + 8) + 'px';
  imgTt.style.top  = (rect.top  + rect.height / 2) + 'px';
});

tbody.addEventListener('mouseleave', () => { imgTt.style.display = 'none'; });

// ─── Rank tooltip (fixed-position) ───────────────────────────────────────────

const rankTt = document.createElement('div');
rankTt.id = 'rank-tooltip';
document.body.appendChild(rankTt);

tbody.addEventListener('mouseover', e => {
  if (window.matchMedia('(max-width: 700px)').matches) return;
  const cell = e.target.closest('td.rank-col');
  if (!cell || !cell.dataset.rankOverall) { rankTt.style.display = 'none'; return; }
  const subRanks = JSON.parse(cell.dataset.subRanks || '{}');
  const subRows = Object.entries(SUB_RANK_LABELS)
    .filter(([k]) => subRanks[k] != null)
    .map(([k, label]) => `<div class="rt-row"><span class="rt-label">${label}</span><span class="rt-val">#${subRanks[k]}</span></div>`)
    .join('');
  rankTt.innerHTML = `<div class="rt-row rt-overall"><span class="rt-label">Overall</span><span class="rt-val">#${cell.dataset.rankOverall}</span></div><div class="rt-divider"></div>${subRows}`;
  rankTt.style.display = 'block';
  const rect = cell.getBoundingClientRect();
  rankTt.style.left = (rect.left + 24) + 'px';
  rankTt.style.top  = (rect.top  + rect.height / 2) + 'px';
});

tbody.addEventListener('mouseleave', () => { rankTt.style.display = 'none'; });

function pickRandom() {
  const rows = filteredRows();
  if (rows.length === 0) return;
  const r = rows[Math.floor(Math.random() * rows.length)];
  const tr = tbody.querySelector('tr[data-id="' + r.id + '"]');
  if (!tr) return;
  tbody.querySelectorAll('tr.picked').forEach(t => t.classList.remove('picked'));
  tr.classList.add('picked');
  tr.scrollIntoView({ block: 'center', behavior: 'smooth' });
  setTimeout(() => tr.classList.remove('picked'), 3000);
  // On mobile, auto-expand the picked card
  if (window.matchMedia('(max-width: 700px)').matches) {
    EXPANDED.add(r.id);
    tr.classList.add('expanded');
  }
}

pickBtn.addEventListener('click', pickRandom);

// ─── Filter reset ────────────────────────────────────────────────────────────
const filterResetBtn = document.getElementById('filterResetBtn');
if (filterResetBtn) {
  filterResetBtn.addEventListener('click', e => {
    e.stopPropagation(); // don't toggle the <details> panel
    update({
      q: DEFAULTS.q,
      hideUnranked: DEFAULTS.hideUnranked,
      players: DEFAULTS.players,
      weight: DEFAULTS.weight,
      timeMax: DEFAULTS.timeMax,
      minRating: DEFAULTS.minRating,
      ratingField: DEFAULTS.ratingField,
      bestOnly: DEFAULTS.bestOnly,
      tags: { ...DEFAULTS.tags },
      mechanics: [...DEFAULTS.mechanics],
      mechanicsMode: DEFAULTS.mechanicsMode,
      categories: [...DEFAULTS.categories],
      categoriesMode: DEFAULTS.categoriesMode,
    });
  });
}

// ─── Scroll to top ────────────────────────────────────────────────────────────

const scrollTopBtn = document.getElementById('scrollTopBtn');
if (scrollTopBtn) {
  window.addEventListener('scroll', () => {
    scrollTopBtn.hidden = window.scrollY < 400;
  }, { passive: true });
  scrollTopBtn.addEventListener('click', () => {
    window.scrollTo({ top: 0, behavior: 'smooth' });
  });
}

// ─── Theme ────────────────────────────────────────────────────────────────────

const THEME_KEY = 'gpw.theme';

function syncThemeIcon() {
  const cur = document.documentElement.getAttribute('data-theme');
  const isDark = cur === 'dark' || (!cur && window.matchMedia('(prefers-color-scheme: dark)').matches);
  themeBtn.textContent = isDark ? '☀' : '🌙';
  const menuItem = document.getElementById('themeMenuItem');
  if (menuItem) menuItem.textContent = isDark ? '☀ Light mode' : '🌙 Dark mode';
}

function applyTheme(theme) {
  if (theme === 'light' || theme === 'dark') document.documentElement.setAttribute('data-theme', theme);
  else document.documentElement.removeAttribute('data-theme');
  syncThemeIcon();
}

function toggleTheme() {
  const cur = document.documentElement.getAttribute('data-theme');
  const isDark = cur === 'dark' || (!cur && window.matchMedia('(prefers-color-scheme: dark)').matches);
  const next = isDark ? 'light' : 'dark';
  applyTheme(next);
  try { localStorage.setItem(THEME_KEY, next); } catch {}
}

themeBtn.addEventListener('click', toggleTheme);

(function initTheme() {
  let saved = null;
  try { saved = localStorage.getItem(THEME_KEY); } catch {}
  applyTheme(saved);
})();

window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', syncThemeIcon);

// ─── PWA install prompt ───────────────────────────────────────────────────────

let deferredInstallPrompt = null;

window.addEventListener('beforeinstallprompt', e => {
  e.preventDefault();
  deferredInstallPrompt = e;
  installBtn.hidden = false;
});

installBtn.addEventListener('click', async () => {
  if (!deferredInstallPrompt) return;
  deferredInstallPrompt.prompt();
  const { outcome } = await deferredInstallPrompt.userChoice;
  if (outcome === 'accepted') {
    installBtn.hidden = true;
    deferredInstallPrompt = null;
  }
});

window.addEventListener('appinstalled', () => {
  installBtn.hidden = true;
  deferredInstallPrompt = null;
});

// Sign-in modal
const signInBtn    = document.getElementById('signInBtn');
const signInModal  = document.getElementById('signInModal');
const signInCancel = document.getElementById('signInCancel');

if (signInBtn && signInModal) {
  signInBtn.addEventListener('click', () => signInModal.showModal());
  signInCancel?.addEventListener('click', () => signInModal.close());
}

// Avatar flyout menu
const authAvatarBtn = document.getElementById('authAvatarBtn');
const authMenu      = document.getElementById('authMenu');

function closeAuthMenu() {
  authMenu?.classList.remove('is-open');
  authAvatarBtn?.setAttribute('aria-expanded', 'false');
}

document.getElementById('themeMenuItem')?.addEventListener('click', () => {
  toggleTheme();
  closeAuthMenu();
});

if (authAvatarBtn && authMenu) {
  authAvatarBtn.addEventListener('click', (e) => {
    e.stopPropagation();
    const open = authMenu.classList.contains('is-open');
    if (open) {
      closeAuthMenu();
    } else {
      authMenu.classList.add('is-open');
      authAvatarBtn.setAttribute('aria-expanded', 'true');
    }
  });

  document.addEventListener('click', () => {
    if (authMenu.classList.contains('is-open')) closeAuthMenu();
  });

  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && authMenu.classList.contains('is-open')) {
      closeAuthMenu();
      authAvatarBtn.focus();
    }
  });
}

// ─── Keyboard shortcuts ───────────────────────────────────────────────────────

document.addEventListener('keydown', e => {
  if (e.metaKey || e.ctrlKey || e.altKey) return;
  const isInput = e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA' || e.target.tagName === 'SELECT';
  if (e.key === 'Escape') {
    if (e.target === search) {
      if (search.value) update({ q: '' });
      else search.blur();
    }
    return;
  }
  if (isInput) return;
  if (e.key === '/') { e.preventDefault(); search.focus(); search.select(); }
  else if (e.key === 'r' || e.key === 'R') { e.preventDefault(); pickRandom(); }
});

// ─── Bootstrap ────────────────────────────────────────────────────────────────

loadTags();
hydrateState();
syncControls();

// Load mechanics list, categories list, and game data in parallel.
// games.json is a superset of bleemus's games.json — all existing
// fields are preserved, plus mechanics, categories, description, thumbnail, etc.
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

  // Compute which sub-rank categories have data for at least one game
  availableSubranks = Object.keys(SUB_RANK_LABELS).filter(k =>
    DATA.some(r => r.subRanks?.[k] != null)
  );

  // Restore any extra rank columns from hydrated state
  renderExtraColHeaders();

  // Wire up multi-select widgets now that MECHANICS / CATEGORIES are loaded
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
