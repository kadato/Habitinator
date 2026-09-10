globalThis.HabitinatorModalSwipe = {
    init: function (element, dotNetRef, options) {
        if (!element || element.dataset.habitinatorModalSwipe === "1") return;
        element.dataset.habitinatorModalSwipe = "1";

        var opts = options || {};
        var allowHorizontal = opts.horizontal !== false;
        var allowDismissDown = opts.dismissDown === true;
        var horizontalMethod = opts.horizontalMethod || "OnModalSwipeAsync";
        var downMethod = opts.downMethod || "OnModalSwipeDownAsync";

        var startX = 0;
        var startY = 0;
        var tracking = false;
        var ignoreSelector = "input, textarea, select, button, a, [role=\"button\"], " +
            "[contenteditable=\"true\"], .mud-popover, .mud-menu-item, .mud-input, .mud-input-slot";

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
            if (!dotNetRef) return;
            var touch = e.changedTouches[0];
            var dx = touch.clientX - startX;
            var dy = touch.clientY - startY;
            if (allowDismissDown && dy > 90 && dy > Math.abs(dx) * 1.4) {
                dotNetRef.invokeMethodAsync(downMethod);
                return;
            }
            if (allowHorizontal && Math.abs(dx) > 60 && Math.abs(dx) > Math.abs(dy) * 1.4) {
                dotNetRef.invokeMethodAsync(horizontalMethod, dx < 0 ? 1 : -1);
            }
        };

        element.addEventListener("touchstart", onStart, { passive: true });
        element.addEventListener("touchend", onEnd, { passive: true });
        element._habitinatorModalSwipeCleanup = function () {
            element.removeEventListener("touchstart", onStart);
            element.removeEventListener("touchend", onEnd);
            delete element.dataset.habitinatorModalSwipe;
        };
    },
    destroy: function (element) {
        if (element?._habitinatorModalSwipeCleanup) {
            element._habitinatorModalSwipeCleanup();
            delete element._habitinatorModalSwipeCleanup;
        }
    }
};
