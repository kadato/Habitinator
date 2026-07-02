// Browser chime + system notification for focus "time's up" (used from Blazor IJSRuntime).
// eslint-disable-next-line no-unused-vars
globalThis.habitinatorFocusTimeUp = function (title, body, playSound, showSystemNotification) {
    if (playSound) {
        playChime();
    }

    if (showSystemNotification) {
        triggerSystemNotification(title, body, "habitinator-focus");
    }
};

// Browser chime + system notification for break completed (used from Blazor IJSRuntime).
// eslint-disable-next-line no-unused-vars
globalThis.habitinatorBreakTimeUp = function (title, body, playSound, showSystemNotification) {
    if (playSound) {
        playBreakChime();
    }

    if (showSystemNotification) {
        triggerSystemNotification(title, body, "habitinator-break");
    }
};

function playChime() {
    try {
        const Ctx = globalThis.AudioContext || globalThis.webkitAudioContext;
        if (Ctx) {
            if (!globalThis._habitinatorAudioCtx) {
                globalThis._habitinatorAudioCtx = new Ctx();
            }
            const ctx = globalThis._habitinatorAudioCtx;
            if (ctx.state === "suspended") {
                ctx.resume();
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
        console.warn("Audio Context playback failed:", e);
    }

    try {
        if (typeof navigator.vibrate === "function") {
            navigator.vibrate([120, 80, 120]);
        }
    } catch (e) {
        console.warn("Vibration failed:", e);
    }
}

function playBreakChime() {
    try {
        const Ctx = globalThis.AudioContext || globalThis.webkitAudioContext;
        if (Ctx) {
            if (!globalThis._habitinatorAudioCtx) {
                globalThis._habitinatorAudioCtx = new Ctx();
            }
            const ctx = globalThis._habitinatorAudioCtx;
            if (ctx.state === "suspended") {
                ctx.resume();
            }
            const t0 = ctx.currentTime;
            // Play a gentle two-tone chime (C5 then E5)
            const tones = [523.25, 659.25];
            for (let i = 0; i < tones.length; i++) {
                const o = ctx.createOscillator();
                const g = ctx.createGain();
                o.type = "sine";
                o.frequency.value = tones[i];
                o.connect(g);
                g.connect(ctx.destination);
                const start = t0 + i * 0.3;
                g.gain.setValueAtTime(0.0001, start);
                g.gain.exponentialRampToValueAtTime(0.15, start + 0.02);
                g.gain.exponentialRampToValueAtTime(0.0001, start + 0.25);
                o.start(start);
                o.stop(start + 0.28);
            }
        }
    } catch (e) {
        console.warn("Audio Context playback failed:", e);
    }

    try {
        if (typeof navigator.vibrate === "function") {
            navigator.vibrate([150, 100, 150]);
        }
    } catch (e) {
        console.warn("Vibration failed:", e);
    }
}

function triggerSystemNotification(title, body, tag = "habitinator-focus") {
    if (typeof Notification === "undefined") {
        return;
    }
    if (Notification.permission === "granted") {
        try {
            new Notification(title, { body, tag, renotify: true });
        } catch (e) {
            console.warn("Notification creation failed:", e);
        }
    } else if (Notification.permission === "default") {
        Notification.requestPermission().then(function (perm) {
            if (perm === "granted") {
                try {
                    new Notification(title, { body, tag });
                } catch (e) {
                    console.warn("Notification creation failed:", e);
                }
            }
        });
    }
}
