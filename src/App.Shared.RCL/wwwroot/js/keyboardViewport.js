// keyboardViewport.js tracks the on-screen keyboard. The height appears as CSS. --kb-height holds the value. `.kb-open` marks the open state. The script keeps the focused field above sticky footers. visualViewport provides the measure when present. Window resize serves as fallback. The script needs no framework.
(function () {
    var root = document.documentElement;
    var lastHeight = 0;
    var raf = 0;

    function setKbHeight(px) {
        var h = Math.max(0, Math.round(px || 0));
        if (h === lastHeight) {
            return;
        }
        lastHeight = h;
        root.style.setProperty('--kb-height', h + 'px');
        if (h > 40) {
            root.classList.add('kb-open');
        } else {
            root.classList.remove('kb-open');
        }
    }

    function measure() {
        try {
            var vv = window.visualViewport;
            if (vv && typeof vv.height === 'number' && typeof window.innerHeight === 'number') {
                // visualViewport shrinks when the keyboard shows. The gap is the keyboard.
                setKbHeight(Math.max(0, window.innerHeight - vv.height - vv.offsetTop));
                return;
            }
        } catch (e) { /* ignore, use fallback */ }
        setKbHeight(0);
    }

    function scheduleMeasure() {
        if (raf) {
            return;
        }
        raf = requestAnimationFrame(function () {
            raf = 0;
            measure();
        });
    }

    function keepFocusedVisible(target) {
        if (!target || !target.scrollIntoView) {
            return;
        }
        // Let the browser move focus first, then nudge the field above the keyboard.
        setTimeout(function () {
            try {
                target.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
            } catch (e) {
                try { target.scrollIntoView(false); } catch (ignored) { /* no-op */ }
            }
        }, 120);
    }

    document.addEventListener('focusin', function (e) {
        var t = e.target;
        if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) {
            scheduleMeasure();
            keepFocusedVisible(t);
        }
    }, { passive: true });

    document.addEventListener('focusout', scheduleMeasure, { passive: true });

    try {
        if (window.visualViewport) {
            window.visualViewport.addEventListener('resize', scheduleMeasure, { passive: true });
            window.visualViewport.addEventListener('scroll', scheduleMeasure, { passive: true });
        }
    } catch (e) { /* older WebViews */ }
    window.addEventListener('resize', scheduleMeasure, { passive: true });
    window.addEventListener('orientationchange', scheduleMeasure, { passive: true });

    measure();
})();
