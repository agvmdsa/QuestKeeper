const CACHE = '__CACHE__';
const SHELL = __SHELL__;

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(CACHE).then((cache) => cache.addAll(SHELL)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((key) => key !== CACHE).map((key) => caches.delete(key))))
      .then(() => self.clients.claim()),
  );
});

// Only the shell is kept: the page itself and files with these extensions, never /api/ and never other origins.
const KEPT = /\.(js|css|png|webmanifest)$/;
// How long the network may take for the shell before the cached copy is shown instead (the network keeps going in the background).
const SHELL_WAIT_MS = 3000;
// This script has no module system and cannot use the page's i18n catalog, so the texts of the page below are plain English constants.
// They are not part of the catalog. The sentence must stay identical to the catalog's `banner.unreachable` (test/sw.test.ts checks it).
const UNREACHABLE = "Can't reach SkuaLink. Check that your PC is on and SkuaLink is running.";
const TRY_AGAIN = 'Try again';
const offline = () => new Response('', { status: 503, statusText: 'Offline' });
// No inline script or style, even though the site's CSP does not apply to a synthesized answer.
const unreachablePage = () =>
  new Response(
    `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>SkuaLink</title></head><body><h1>SkuaLink</h1><p>${UNREACHABLE}</p><p><a href="/">${TRY_AGAIN}</a></p></body></html>`,
    {
      status: 503,
      statusText: 'Unreachable',
      headers: { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store', 'Referrer-Policy': 'no-referrer', 'Content-Security-Policy': "default-src 'none'" },
    },
  );

const TIMED_OUT = Symbol('timed out');

/** What to show when the network gave no usable answer: the cached copy, else the page for a navigation, else a plain failure. */
async function fallback(request) {
  const hit = await caches.match(request);
  if (hit) return hit;
  // A missing script or style must fail as such, not be answered with the page's HTML.
  if (request.destination === 'script' || request.destination === 'style') return offline();
  if (request.mode === 'navigate' || request.destination === 'document') return (await caches.match('/')) || unreachablePage();
  return offline();
}

self.addEventListener('fetch', (event) => {
  const request = event.request;
  const url = new URL(request.url);
  if (request.method !== 'GET' || url.origin !== self.location.origin || url.pathname.startsWith('/api/')) return;
  const keep = url.pathname === '/' || KEPT.test(url.pathname);
  // The network request runs to its end even after the cache has answered, so the cache is refreshed for the next visit.
  let stored = Promise.resolve();
  const network = fetch(request).then((response) => {
    if (keep && response.ok && response.type === 'basic') {
      const copy = response.clone();
      // Not awaited: the answer is not delayed by the cache write.
      stored = caches.open(CACHE).then((cache) => cache.put(request, copy)).catch(() => undefined);
    }
    return response;
  });
  event.waitUntil(network.then(() => stored, () => undefined));
  if (!keep) {
    event.respondWith(network.catch(() => fallback(request)));
    return;
  }
  const wait = async () => {
    // The timer only applies where there is something to fall back to: a cached copy, or a navigation (which gets the plain page).
    // Without one, a slow network is waited for as before, so a new version's assets on a slow link still load.
    const canFallBack = request.mode === 'navigate' || request.destination === 'document' || !!(await caches.match(request));
    if (!canFallBack) return network.catch(() => fallback(request));
    let timer;
    const late = new Promise((resolve) => {
      timer = setTimeout(() => resolve(TIMED_OUT), SHELL_WAIT_MS);
    });
    try {
      const result = await Promise.race([network, late]);
      return result === TIMED_OUT ? await fallback(request) : result;
    } catch {
      return fallback(request);
    } finally {
      clearTimeout(timer);
    }
  };
  event.respondWith(wait());
});
