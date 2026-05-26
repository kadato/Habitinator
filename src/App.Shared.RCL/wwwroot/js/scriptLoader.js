window.habitinatorLoadScript = function (src) {
    var existing = document.querySelector('script[data-habitinator-src="' + src + '"]');
    if (existing) {
        if (existing.getAttribute('data-habitinator-loaded') === '1') {
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
        var s = document.createElement('script');
        s.src = src;
        s.async = true;
        s.setAttribute('data-habitinator-src', src);
        s.onload = function () {
            s.setAttribute('data-habitinator-loaded', '1');
            resolve();
        };
        s.onerror = function () {
            reject(new Error('Failed to load ' + src));
        };
        document.body.appendChild(s);
    });
};
