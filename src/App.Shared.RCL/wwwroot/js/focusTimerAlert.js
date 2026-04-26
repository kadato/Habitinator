// Browser chime + system notification for focus "time's up" (used from Blazor IJSRuntime).
// eslint-disable-next-line no-unused-vars
window.habitinatorFocusTimeUp = function (title, body, playSound, showSystemNotification) {
    if (playSound) {
        try {
            const Ctx = window.AudioContext || window.webkitAudioContext;
            if (Ctx) {
                if (!window._habitinatorAudioCtx) {
                    window._habitinatorAudioCtx = new Ctx();
                }
                const ctx = window._habitinatorAudioCtx;
                if (ctx.state === "suspended") {
                    void ctx.resume();
                }
                const t0 = ctx.currentTime;
                for (let i = 0; i < 3; i++) {
                    const o = ctx.createOscillator();
                    const g = ctx.createGain();
                    o.type = "sine";
                    o.frequency.value = 880;
                    o.connect(g);
                    g.connect(ctx.destination);
                    const start = t0 + i * 0.22;
                    g.gain.setValueAtTime(0.0001, start);
                    g.gain.exponentialRampToValueAtTime(0.18, start + 0.01);
                    g.gain.exponentialRampToValueAtTime(0.0001, start + 0.14);
                    o.start(start);
                    o.stop(start + 0.15);
                }
            }
        } catch (e) {
            /* no-op: autoplay or embed policies */
        }
        try {
            if (typeof navigator.vibrate === "function") {
                navigator.vibrate([120, 80, 120]);
            }
        } catch (e) {
        }
    }

    if (!showSystemNotification) {
        return;
    }
    if (typeof Notification === "undefined") {
        return;
    }
    if (Notification.permission === "granted") {
        try {
            new Notification(title, { body, tag: "habitinator-focus", renotify: true });
        } catch (e) {
        }
    } else if (Notification.permission === "default") {
        void Notification.requestPermission().then(function (perm) {
            if (perm === "granted") {
                try {
                    new Notification(title, { body, tag: "habitinator-focus" });
                } catch (e) {
                }
            }
        });
    }
};
