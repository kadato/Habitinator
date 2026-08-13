const CACHE_NAME = 'habitinator-v3';
const FRAMEWORK_CACHE = 'habitinator-framework-v3';

// Shell assets: always pre-cached for instant offline rendering
const SHELL_ASSETS = [
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

// Framework assets pattern: match all .wasm and .js files under _framework/
const FRAMEWORK_ASSET_PATTERN = /^\/_framework\/(.+\.(wasm|js))$/i;
const CONTENT_ASSET_PATTERN = /^\/_content\/(.+\.(wasm|js|css|woff2?))$/i;

globalThis.addEventListener('install', event => {
    event.waitUntil(
        caches.open(CACHE_NAME).then(cache => {
            return cache.addAll(SHELL_ASSETS);
        }).then(() => globalThis.skipWaiting())
    );
});

globalThis.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys().then(cacheNames => {
            return Promise.all(
                cacheNames.map(cacheName => {
                    if (cacheName !== CACHE_NAME && cacheName !== FRAMEWORK_CACHE) {
                        console.log('Habitinator SW: deleting old cache:', cacheName);
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

    // -- Framework assets: cache-first with network update. They are immutable and fingerprinted. --
    if (FRAMEWORK_ASSET_PATTERN.test(url.pathname) || CONTENT_ASSET_PATTERN.test(url.pathname)) {
        event.respondWith(
            caches.open(FRAMEWORK_CACHE).then(cache =>
                cache.match(event.request).then(cached => {
                    const networkFetch = fetch(event.request).then(response => {
                        if (response?.status === 200) {
                            cache.put(event.request, response.clone());
                        }
                        return response;
                    }).catch(() => cached);

                    // Return cached immediately, update cache in background
                    return cached || networkFetch;
                })
            )
        );
        return;
    }

    // -- Navigation requests: network-first, fall back to cached shell --
    if (event.request.mode === 'navigate') {
        event.respondWith(
            fetch(event.request).catch(() => {
                return caches.match('/');
            })
        );
        return;
    }

    // -- Other assets: cache-first --
    event.respondWith(
        caches.match(event.request).then(cachedResponse => {
            if (cachedResponse) {
                return cachedResponse;
            }

            return fetch(event.request).then(networkResponse => {
                if (networkResponse?.status === 200 && networkResponse?.type === 'basic') {
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
