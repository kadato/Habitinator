// Overlay-first back handling shared by web history and MAUI native back.
//
// Web mode (init with history=true): every overlay open (Mud dialog, command
// palette, AI drawer) pushes a same-URL history entry. An OS back gesture or the
// browser back button then pops the overlay entry instead of leaving the page.
// popstate asks .NET to close the top overlay. A programmatic close (save, X,
// backdrop tap) removes its entry with history.back() under suppressNextPop.
// Menu popovers never push; back closes them through tryCloseTopOverlay.
//
// MAUI mode (history=false): no history entries. MainActivity forwards the press
// to .NET, which closes overlays in priority order and only exits on a confirmed
// double press. tryCloseTopOverlay clicks the top dialog close button or its
// backdrop, real clicks so Blazor routes them, no synthetic keyboard events.
//
// Edge swipes belong to the OS. boardSwipe.js and modalSwipe.js ignore touches
// that start within EDGE_PX of the left or right screen edge, so a system back
// swipe never moves a board column or calendar by accident.
globalThis.HabitinatorMobileBack = (function () {
    var EDGE_PX = 24;
    var dotNetRef = null;
    var historyEnabled = false;
    var depth = 0;
    var prevCount = 0;
    var suppressNextPop = false;
    var expectingDomRemoval = false;
    var expectTimer = 0;
    var observer = null;

    function isDrawerOpen(el) {
        return el && el.classList &&
            (el.classList.contains('mud-drawer--open') || el.getAttribute('aria-hidden') === 'false');
    }

    function overlayCount() {
        try {
            var dialogs = document.querySelectorAll('.mud-dialog').length;
            var palette = document.querySelector('.cmd-palette-backdrop') ? 1 : 0;
            var drawer = null;
            var candidates = document.querySelectorAll('.ai-assistant-drawer');
            for (var i = 0; i < candidates.length; i++) {
                if (isDrawerOpen(candidates[i])) {
                    drawer = 1;
                    break;
                }
            }
            // Any other temporary drawer, such as a nav drawer, counts too.
            if (!drawer && document.querySelector('.mud-drawer-temporary.mud-drawer--open')) {
                drawer = 1;
            }
            return dialogs + palette + (drawer ? 1 : 0);
        } catch (e) {
            return 0;
        }
    }

    function pushOne() {
        depth++;
        try {
            history.pushState({ habitinatorOverlay: depth }, document.title);
        } catch (e) {
            depth--;
        }
    }

    function onClosedOne() {
        if (expectingDomRemoval) {
            return;
        }
        if (depth > 0) {
            depth--;
            suppressNextPop = true;
            try {
                history.back();
            } catch (e) {
                suppressNextPop = false;
            }
        }
    }

    function recount() {
        var n = overlayCount();
        if (n === prevCount) {
            return;
        }
        if (n > prevCount) {
            for (var i = prevCount; i < n; i++) {
                pushOne();
            }
        } else {
            for (var j = n; j < prevCount; j++) {
                onClosedOne();
            }
        }
        prevCount = n;
    }

    function onPopState() {
        if (!historyEnabled) {
            return;
        }
        if (suppressNextPop) {
            suppressNextPop = false;
            return;
        }
        if (depth > 0) {
            depth--;
            expectingDomRemoval = true;
            if (expectTimer) {
                clearTimeout(expectTimer);
            }
            expectTimer = setTimeout(function () {
                expectingDomRemoval = false;
            }, 800);
            try {
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('OnHistoryBackAsync');
                }
            } catch (e) { /* .NET closes the top overlay; close failures keep the page */ }
        }
        // depth 0 means a real navigation. Blazor's router handles it.
    }

    function topDialog() {
        var dialogs = document.querySelectorAll('.mud-dialog');
        return dialogs.length ? dialogs[dialogs.length - 1] : null;
    }

    function clickBackdropFor(dlg) {
        var container = dlg.closest ? dlg.closest('.mud-dialog-container') : null;
        if (container) {
            container.click();
            return true;
        }
        dlg.click();
        return true;
    }

    return {
        edgeGuardPx: EDGE_PX,

        // True when a touch starting at clientX belongs to the OS edge gesture.
        isEdgeSwipe: function (clientX) {
            if (typeof clientX !== 'number') {
                return false;
            }
            if (clientX < EDGE_PX) {
                return true;
            }
            try {
                var w = globalThis.innerWidth || 0;
                return w > 0 && clientX > w - EDGE_PX;
            } catch (e) {
                return false;
            }
        },

        init: function (ref, withHistory) {
            dotNetRef = ref || null;
            historyEnabled = withHistory !== false;
            prevCount = 0;
            if (!historyEnabled) {
                return;
            }
            try {
                window.removeEventListener('popstate', onPopState);
                window.addEventListener('popstate', onPopState);
            } catch (e) { /* older WebViews */ }
            if (observer) {
                observer.disconnect();
            }
            if (!document.body) {
                return;
            }
            observer = new MutationObserver(recount);
            observer.observe(document.body, {
                childList: true,
                subtree: true,
                attributes: true,
                attributeFilter: ['class', 'aria-hidden']
            });
            recount();
        },

        // Blazor navigated while overlays were tracked. Stale same-URL entries
        // stay below the new page, so drop the counter without touching history.
        // A programmatic close before navigation already popped its entry.
        syncAfterNavigation: function () {
            expectingDomRemoval = false;
            prevCount = overlayCount();
            depth = Math.min(depth, prevCount);
        },

        hasOverlay: function () {
            return overlayCount() > 0;
        },

        // Closes the top overlay with real clicks so Blazor routes them.
        // Order: dialog close button, dialog backdrop, menu popover overlay.
        tryCloseTopOverlay: function () {
            var dlg = topDialog();
            if (dlg) {
                var btn = dlg.querySelector(
                    '.hab-modal__close:not([disabled]), ' +
                    '.edit-daily-header__close:not([disabled]), ' +
                    '.mud-button-close:not([disabled])');
                if (btn) {
                    btn.click();
                    return true;
                }
                return clickBackdropFor(dlg);
            }
            try {
                var popOverlay = document.querySelector('.mud-overlay-popover, .mud-popover-fixed');
                if (popOverlay && popOverlay.getClientRects().length > 0) {
                    popOverlay.click();
                    return true;
                }
                var popover = document.querySelector('.mud-popover.mud-popover-open');
                if (popover) {
                    var esc = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
                    popover.dispatchEvent(esc);
                    return true;
                }
            } catch (e) { /* fall through to the count check */ }
            return overlayCount() > 0;
        }
    };
})();
