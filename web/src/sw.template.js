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
const offline = () => new Response('', { status: 503, statusText: 'Offline' });

self.addEventListener('fetch', (event) => {
  const request = event.request;
  const url = new URL(request.url);
  if (request.method !== 'GET' || url.origin !== self.location.origin || url.pathname.startsWith('/api/')) return;
  const keep = url.pathname === '/' || KEPT.test(url.pathname);
  event.respondWith(
    fetch(request)
      .then((response) => {
        if (keep && response.ok && response.type === 'basic') {
          const copy = response.clone();
          event.waitUntil(caches.open(CACHE).then((cache) => cache.put(request, copy)));
        }
        return response;
      })
      .catch(() =>
        caches.match(request).then((hit) => {
          if (hit) return hit;
          // A missing script or style must fail as such, not be answered with the page's HTML.
          if (request.destination === 'script' || request.destination === 'style') return offline();
          return caches.match('/').then((page) => page || offline());
        }),
      ),
  );
});
