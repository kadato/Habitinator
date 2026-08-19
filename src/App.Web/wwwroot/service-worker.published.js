const CACHE_NAME = 'habitinator-v4';
const FRAMEWORK_CACHE = 'habitinator-framework-v4';

// Shell assets are always precached for offline rendering.
const SHELL_ASSETS = [
    '/',
    '/manifest.webmanifest',
    '/favicon.ico',
    '/favicon.png',
    '/favicon.svg',
    '/apple-touch-icon.png',
    '/_content/App.Shared.RCL/css/tokens.css',
    '/_content/App.Shared.RCL/css/typography.css',
    '/_content/App.Shared.RCL/css/app.css',
    '/_content/App.Shared.RCL/css/dialogs.css',
    '/_content/App.Shared.RCL/css/auth-pages.css',
    '/_content/MudBlazor/MudBlazor.min.css',
    '/_content/MudBlazor/MudBlazor.min.js',
    '/_framework/blazor.web.js',
    '/_content/App.Shared.RCL/fonts/PlusJakartaSans-Regular.woff2',
    '/_content/App.Shared.RCL/fonts/PlusJakartaSans-Medium.woff2',
    '/_content/App.Shared.RCL/fonts/PlusJakartaSans-Bold.woff2'
];

// Match all wasm and js files under the framework directory.
const FRAMEWORK_ASSET_PATTERN = /^\/_framework\/(.+\.(wasm|js))$/i;
const CONTENT_ASSET_PATTERN = /^\/_content\/(.+\.(wasm|js|css|woff2?))$/i;

globalThis.addEventListener('install', event => {
    event.waitUntil(
        caches.open(CACHE_NAME).then(async cache => {
            await Promise.allSettled(
                SHELL_ASSETS.map(async url => {
                    try {
                        const response = await fetch(url, { cache: 'reload' });
                        if (response.ok) {
                            await cache.put(url, response);
                        } else {
                            console.warn(`Habitinator SW: Failed to precache ${url} (status: ${response.status})`);
                        }
                    } catch (err) {
                        console.warn(`Habitinator SW: Network error precaching ${url}:`, err);
                    }
                })
            );
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

    // Allow the browser to fulfill dotnet runtime scripts directly from the document link preload cache
    // to avoid Chromium cross-world service worker resource mismatch warnings.
    if (/^\/_framework\/dotnet\..*\.js$/i.test(url.pathname)) {
        return;
    }

    // Framework assets are cache-first because they are immutable and fingerprinted.
    if (FRAMEWORK_ASSET_PATTERN.test(url.pathname) || CONTENT_ASSET_PATTERN.test(url.pathname)) {
        event.respondWith(
            caches.open(FRAMEWORK_CACHE).then(cache =>
                cache.match(event.request).then(cached => {
                    if (cached) {
                        return cached;
                    }
                    return fetch(event.request).then(response => {
                        if (response?.status === 200) {
                            cache.put(event.request, response.clone());
                        }
                        return response;
                    });
                })
            )
        );
        return;
    }

    // Navigation requests are network-first and fall back to the cached shell.
    if (event.request.mode === 'navigate') {
        event.respondWith(
            fetch(event.request).catch(() => {
                return caches.match('/');
            })
        );
        return;
    }

    // Other assets use cache-first.
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
