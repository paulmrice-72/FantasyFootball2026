// Minimal PWA service worker — cache shell assets on install
const CACHE = 'fc-ai-v1';
const SHELL = ['/', '/index.html', '/css/app.css'];

self.addEventListener('install', e =>
    e.waitUntil(caches.open(CACHE).then(c => c.addAll(SHELL))));

self.addEventListener('fetch', e =>
    e.respondWith(caches.match(e.request).then(r => r || fetch(e.request))));