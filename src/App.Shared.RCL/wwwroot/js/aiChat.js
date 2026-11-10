// Chat behavior: tap a bubble on touch to reveal its copy/edit buttons,
// follow streamed tokens only while the reader stays near the bottom.
globalThis.HabitinatorAiChat = {
    initScrollArea: function () {
        var scrollArea = document.getElementById("ai-messages-scroll-area");
        if (scrollArea) {
            globalThis.HabitinatorAiChat.init(scrollArea);
        }
        globalThis.HabitinatorAiChat.initGlobalDismiss();
    },
    init: function (scrollArea) {
        if (!scrollArea || scrollArea.dataset.habitinatorAiChat === "1") return;
        scrollArea.dataset.habitinatorAiChat = "1";

        var interactive = "button, a, input, textarea, select, [role=\"button\"], " +
            "[contenteditable=\"true\"], .mud-popover, .mud-menu-item";

        // Tap (not scroll, not swipe) toggles one bubble's actions.
        var downX = 0;
        var downY = 0;
        scrollArea.addEventListener("pointerdown", function (e) {
            try {
                if (e.pointerType !== "touch" && e.pointerType !== "pen") return;
                downX = e.clientX;
                downY = e.clientY;
            } catch (ignored) { /* never break taps */ }
        }, { passive: true });

        scrollArea.addEventListener("pointerup", function (e) {
            try {
                if (e.pointerType !== "touch" && e.pointerType !== "pen") return;
                var dx = e.clientX - downX;
                var dy = e.clientY - downY;
                if (dx * dx + dy * dy > 144) return; // moved: it was a scroll
                var t = e.target;
                if (!t || !t.closest) return;
                if (t.closest(interactive)) return;
                var message = t.closest(".ai-message");
                if (!message || !scrollArea.contains(message)) return;
                var wasOpen = message.classList.contains("show-actions");
                var open = scrollArea.querySelectorAll(".ai-message.show-actions");
                for (var i = 0; i < open.length; i++) open[i].classList.remove("show-actions");
                if (!wasOpen) message.classList.add("show-actions");
            } catch (ignored) { /* never break taps */ }
        });

        // Reaching the bottom by hand dismisses the jump button.
        scrollArea.addEventListener("scroll", function () {
            try {
                if (globalThis.HabitinatorAiChat.isNearBottom(scrollArea)) {
                    globalThis.HabitinatorAiChat.hideJump(scrollArea);
                }
            } catch (ignored) { /* scroll hint is best effort */ }
        }, { passive: true });
    },
    area: function () {
        return document.getElementById("ai-messages-scroll-area");
    },
    isNearBottom: function (area) {
        if (!area) return true;
        return area.scrollHeight - area.scrollTop - area.clientHeight < 120;
    },
    scrollToBottom: function () {
        try {
            var area = globalThis.HabitinatorAiChat.area();
            if (!area) return;
            globalThis.HabitinatorAiChat.hideJump(area);
            area.scrollTop = area.scrollHeight;
        } catch (ignored) { /* scroll is best effort */ }
    },
    scrollIfNear: function () {
        try {
            var area = globalThis.HabitinatorAiChat.area();
            if (!area) return;
            if (globalThis.HabitinatorAiChat.isNearBottom(area)) {
                area.scrollTop = area.scrollHeight;
                globalThis.HabitinatorAiChat.hideJump(area);
            } else {
                globalThis.HabitinatorAiChat.showJump(area);
            }
        } catch (ignored) { /* scroll is best effort */ }
    },
    showJump: function (area) {
        var btn = area.querySelector(":scope > .ai-jump-latest");
        if (!btn) {
            btn = document.createElement("button");
            btn.type = "button";
            btn.className = "ai-jump-latest";
            btn.textContent = "↓ Latest";
            btn.setAttribute("aria-label", "Jump to latest message");
            btn.addEventListener("click", function () {
                globalThis.HabitinatorAiChat.scrollToBottom();
            });
            area.appendChild(btn);
        }
        btn.hidden = false;
    },
    hideJump: function (area) {
        var scope = area || document;
        var btn = scope.querySelector ? scope.querySelector(".ai-jump-latest") : null;
        if (btn) btn.hidden = true;
    },
    clearActions: function () {
        try {
            var open = document.querySelectorAll(".ai-drawer-container .ai-message.show-actions");
            for (var i = 0; i < open.length; i++) open[i].classList.remove("show-actions");
            var active = document.activeElement;
            if (active && active.closest && active.closest(".ai-drawer-container")) {
                active.blur();
            }
        } catch (ignored) { /* clearing is best effort */ }
    },
    initGlobalDismiss: function () {
        try {
            if (document.documentElement.dataset.habitinatorAiDismiss === "1") return;
            document.documentElement.dataset.habitinatorAiDismiss = "1";
            var downX = 0;
            var downY = 0;
            document.addEventListener("pointerdown", function (e) {
                try {
                    if (e.pointerType !== "touch" && e.pointerType !== "pen") return;
                    downX = e.clientX;
                    downY = e.clientY;
                } catch (ignored) { /* never break taps */ }
            }, { passive: true });
            document.addEventListener("pointerup", function (e) {
                try {
                    if (e.pointerType !== "touch" && e.pointerType !== "pen") return;
                    var dx = e.clientX - downX;
                    var dy = e.clientY - downY;
                    if (dx * dx + dy * dy > 144) return; // moved: it was a scroll
                    var t = e.target;
                    if (!t || !t.closest) return;
                    if (t.closest(".ai-message")) return; // bubbles toggle themselves
                    globalThis.HabitinatorAiChat.clearActions();
                } catch (ignored) { /* never break taps */ }
            }, { passive: true });
        } catch (ignored) { /* dismiss is best effort */ }
    }
};
