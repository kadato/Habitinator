globalThis.habitinatorLoadScript = function (src) {
    const resolvedSrc = (globalThis.habitinatorAssets?.[src]) || src;
    const existing = document.querySelector('script[data-habitinator-src="' + src + '"]');
    if (existing) {
        if (existing.dataset.habitinatorLoaded === '1') {
            return Promise.resolve();
        }

        return new Promise(function (resolve, reject) {
            existing.addEventListener('load', function () { resolve(); }, { once: true });
            existing.addEventListener('error', function () {
                reject(new Error('Failed to load ' + src));
            }, { once: true });
        });
    }

    return new Promise(function (resolve, reject) {
        const s = document.createElement('script');
        s.src = resolvedSrc;
        s.async = true;
        s.dataset.habitinatorSrc = src;
        s.onload = function () {
            s.dataset.habitinatorLoaded = '1';
            resolve();
        };
        s.onerror = function () {
            reject(new Error('Failed to load ' + src));
        };
        document.body.appendChild(s);
    });
};
