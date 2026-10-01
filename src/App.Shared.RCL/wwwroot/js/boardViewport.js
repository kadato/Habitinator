// Single-render viewport source for the board. MainBoard renders either the mobile
// tabbed column or the desktop three-column grid, never both, so hidden columns
// never mount, never init SortableJS, and never double-handle events.
globalThis.HabitinatorBoardViewport = (function () {
  var query = "(max-width: 959.98px)";
  var media = null;
  var dotNetHelper = null;

  function current() {
    if (typeof globalThis.matchMedia === "function") {
      return globalThis.matchMedia(query).matches;
    }
    return (globalThis.innerWidth || 1024) < 960;
  }

  function onChange() {
    if (dotNetHelper) {
      dotNetHelper.invokeMethodAsync("OnViewportChanged", current()).catch(function () {});
    }
  }

  return {
    isMobile: function () {
      return current();
    },
    watch: function (dotNetRef) {
      dotNetHelper = dotNetRef;
      if (typeof globalThis.matchMedia === "function") {
        media = globalThis.matchMedia(query);
        if (media.addEventListener) {
          media.addEventListener("change", onChange);
        } else if (media.addListener) {
          media.addListener(onChange);
        }
      } else {
        globalThis.addEventListener("resize", onChange);
      }
    },
    unwatch: function () {
      if (media) {
        if (media.removeEventListener) {
          media.removeEventListener("change", onChange);
        } else if (media.removeListener) {
          media.removeListener(onChange);
        }
        media = null;
      } else {
        globalThis.removeEventListener("resize", onChange);
      }
      dotNetHelper = null;
    }
  };
})();
