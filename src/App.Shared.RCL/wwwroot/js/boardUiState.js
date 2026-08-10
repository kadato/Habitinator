// Column filter state + onboarding flags + data export download, persisted in localStorage.
globalThis.habitinatorGetColumnFilterState = function (key) {
    try {
        const raw = window.localStorage.getItem(key);
        return raw ? JSON.parse(raw) : null;
    } catch {
        return null;
    }
};

globalThis.habitinatorSetColumnFilterState = function (key, value) {
    try {
        if (value === null || value === undefined) {
            window.localStorage.removeItem(key);
        } else {
            window.localStorage.setItem(key, JSON.stringify(value));
        }
        return true;
    } catch {
        return false;
    }
};

globalThis.habitinatorGetOnboardingDone = function (key) {
    try {
        return window.localStorage.getItem(key) === '1';
    } catch {
        return false;
    }
};

globalThis.habitinatorSetOnboardingDone = function (key, done) {
    try {
        if (done) {
            window.localStorage.setItem(key, '1');
        } else {
            window.localStorage.removeItem(key);
        }
        return true;
    } catch {
        return false;
    }
};

// Downloads a JSON payload as a file. Returns false when the environment does not
// support programmatic downloads (e.g. MAUI WebView), so callers can fall back.
globalThis.habitinatorDownloadJson = function (fileName, jsonText) {
    try {
        const blob = new Blob([jsonText], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        a.style.display = 'none';
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(function () { URL.revokeObjectURL(url); }, 0);
        return true;
    } catch {
        return false;
    }
};

globalThis.habitinatorCopyText = async function (text) {
    try {
        if (navigator.clipboard?.writeText) {
            await navigator.clipboard.writeText(text);
            return true;
        }
    } catch {
        // fall through to the execCommand path
    }
    try {
        const ta = document.createElement('textarea');
        ta.value = text;
        ta.style.position = 'fixed';
        ta.style.opacity = '0';
        document.body.appendChild(ta);
        ta.select();
        const ok = document.execCommand('copy'); // NOSONAR: legacy fallback when the Clipboard API is unavailable
        ta.remove();
        return ok;
    } catch {
        return false;
    }
};
