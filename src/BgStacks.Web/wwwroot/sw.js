const CACHE_VERSION = 'bg-stacks-v1';

// App shell assets — static, safe to pre-cache on install
const PRECACHE = [
  '/',
  '/index.html',
  '/app.js',
  '/styles.css',
  '/manifest.json',
  '/icon.svg',
  '/icon-192.png',
  '/icon-512.png',
];

// Dynamic data endpoints — served network-first; only 200 responses are cached
// (202 "still loading" must never be cached or they loop forever)
const DATA_ENDPOINTS = ['/games.json', '/mechanics.json', '/categories.json', '/event.json'];

// ─── Install: pre-cache app shell ────────────────────────────────────────────

self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(CACHE_VERSION)
      .then(cache => cache.addAll(PRECACHE))
      .then(() => self.skipWaiting())
  );
});

// ─── Activate: purge old caches ───────────────────────────────────────────────

self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys()
      .then(keys => Promise.all(
        keys.filter(k => k !== CACHE_VERSION).map(k => caches.delete(k))
      ))
      .then(() => self.clients.claim())
  );
});

// ─── Fetch ────────────────────────────────────────────────────────────────────

self.addEventListener('fetch', event => {
  const { request } = event;

  if (request.method !== 'GET') return;

  if (!request.url.startsWith(self.location.origin)) return;

  if (new URL(request.url).pathname.startsWith('/.auth')) return;

  const pathname = new URL(request.url).pathname;

  // Data endpoints: network-first, cache only 200s, fall back to cache offline
  if (DATA_ENDPOINTS.some(p => pathname === p || pathname.endsWith(p))) {
    event.respondWith(
      fetch(request).then(response => {
        if (response.status === 200) {
          const clone = response.clone();
          caches.open(CACHE_VERSION).then(c => c.put(request, clone));
        }
        return response;
      }).catch(() => caches.match(request))
    );
    return;
  }

  // App shell: cache-first, stale-while-revalidate for HTML/JS/CSS
  event.respondWith(
    caches.match(request).then(cached => {
      if (cached) {
        const ext = pathname.split('.').pop();
        if (['html', 'js', 'css'].includes(ext) || request.url.endsWith('/')) {
          fetch(request).then(response => {
            if (response.status === 200) {
              const clone = response.clone();
              caches.open(CACHE_VERSION).then(c => c.put(request, clone));
            }
          }).catch(() => {});
        }
        return cached;
      }

      return fetch(request).then(response => {
        if (response.status === 200) {
          const clone = response.clone();
          caches.open(CACHE_VERSION).then(c => c.put(request, clone));
        }
        return response;
      });
    })
  );
});
