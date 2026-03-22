const CACHE = 'fc-ai-v1';
const SHELL = ['/', '/index.html', '/css/app.css'];

self.addEventListener('install', e =>
    e.waitUntil(caches.open(CACHE).then(c => c.addAll(SHELL))));

self.addEventListener('fetch', e => {
    // Never intercept API calls or cross-origin requests
    const url = new URL(e.request.url);
    if (url.origin !== location.origin || url.pathname.startsWith('/api/')) {
        return; // Let the browser handle it normally
    }

    e.respondWith(caches.match(e.request).then(r => r || fetch(e.request)));
});