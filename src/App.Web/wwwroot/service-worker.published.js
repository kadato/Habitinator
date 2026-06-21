const CACHE_NAME = 'habitinator-v2';
const PRE_CACHE_ASSETS = [
    '/',
    '/manifest.webmanifest',
    '/favicon.ico',
    '/favicon.png',
    '/favicon.svg',
    '/apple-touch-icon.png',
    '/app.css',
    '/_content/App.Shared.RCL/css/typography.css',
    '/_content/App.Shared.RCL/css/auth-pages.css',
    '/_content/MudBlazor/MudBlazor.min.css',
    '/_content/MudBlazor/MudBlazor.min.js',
    '/_framework/blazor.web.js',
    '/_content/App.Shared.RCL/fonts/PlusJakartaSans-Regular.woff2',
    '/_content/App.Shared.RCL/fonts/PlusJakartaSans-Medium.woff2',
    '/_content/App.Shared.RCL/fonts/PlusJakartaSans-Bold.woff2'
];

globalThis.addEventListener('install', event => {
    event.waitUntil(
        caches.open(CACHE_NAME).then(cache => {
            return cache.addAll(PRE_CACHE_ASSETS);
        }).then(() => globalThis.skipWaiting())
    );
});

globalThis.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys().then(cacheNames => {
            return Promise.all(
                cacheNames.map(cacheName => {
                    if (cacheName !== CACHE_NAME) {
                        console.log('Deleting old cache:', cacheName);
                        return caches.delete(cacheName);
                    }
                })
            );
        }).then(() => globalThis.clients.claim())
    );
});

globalThis.addEventListener('fetch', event => {
    if (event.request.method !== 'GET') {
        return;
    }

    const url = new URL(event.request.url);

    // Only cache requests from our origin
    if (url.origin !== globalThis.location.origin) {
        return;
    }

    // Exclude API requests from caching
    if (url.pathname.startsWith('/api/')) {
        return;
    }

    // For navigation requests, try the network first to get the latest server-rendered content,
    // but fall back to the cached index/shell if offline.
    if (event.request.mode === 'navigate') {
        event.respondWith(
            fetch(event.request).catch(() => {
                return caches.match('/');
            })
        );
        return;
    }

    // For other assets, use cache-first strategy.
    // This includes Blazor framework files, DLLs, static assets, etc.
    event.respondWith(
        caches.match(event.request).then(cachedResponse => {
            if (cachedResponse) {
                return cachedResponse;
            }

            return fetch(event.request).then(networkResponse => {
                // Check if we received a valid response
                if (networkResponse?.status === 200 && networkResponse?.type === 'basic') {
                    // Cache the newly retrieved asset
                    const responseToCache = networkResponse.clone();
                    caches.open(CACHE_NAME).then(cache => {
                        cache.put(event.request, responseToCache);
                    });
                }

                return networkResponse;
            });
        })
    );
});
