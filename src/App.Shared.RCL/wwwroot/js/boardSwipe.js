globalThis.HabitinatorBoardSwipe = {
    init: function (element, dotNetRef) {
        if (!element || element.dataset.habitinatorSwipe === "1") return;
        element.dataset.habitinatorSwipe = "1";

        var startX = 0;
        var startY = 0;
        var tracking = false;
        var ignoreSelector = "input, textarea, select, button, a, [role=\"button\"], " +
            "[contenteditable=\"true\"], .mud-popover, .mud-menu-item, .mud-input";

        var onStart = function (e) {
            if (e.changedTouches?.length !== 1) {
                tracking = false;
                return;
            }
            var t = e.target;
            if (t?.closest?.(ignoreSelector)) {
                tracking = false;
                return;
            }
            var touch = e.changedTouches[0];
            startX = touch.clientX;
            startY = touch.clientY;
            tracking = true;
        };

        var onEnd = function (e) {
            if (!tracking || !e.changedTouches || e.changedTouches.length === 0) return;
            tracking = false;
            var touch = e.changedTouches[0];
            var dx = touch.clientX - startX;
            var dy = touch.clientY - startY;
            if (Math.abs(dx) < 60 || Math.abs(dx) < Math.abs(dy) * 1.4) return;
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync("OnSwipeSectionAsync", dx < 0 ? 1 : -1);
            }
        };

        element.addEventListener("touchstart", onStart, { passive: true });
        element.addEventListener("touchend", onEnd, { passive: true });
        element._habitinatorSwipeCleanup = function () {
            element.removeEventListener("touchstart", onStart);
            element.removeEventListener("touchend", onEnd);
            delete element.dataset.habitinatorSwipe;
        };
    },
    destroy: function (element) {
        if (element?._habitinatorSwipeCleanup) {
            element._habitinatorSwipeCleanup();
            delete element._habitinatorSwipeCleanup;
        }
    }
};
