// In development, always fetch from the network and do not cache assets.
globalThis.addEventListener('install', event => {
    globalThis.skipWaiting();
});

globalThis.addEventListener('activate', event => {
    event.waitUntil(globalThis.clients.claim());
});
