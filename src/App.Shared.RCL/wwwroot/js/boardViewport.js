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
        if (media && typeof media.addEventListener === "function") {
          media.addEventListener("change", onChange);
        } else {
          globalThis.addEventListener("resize", onChange);
        }
      } else {
        globalThis.addEventListener("resize", onChange);
      }
    },
    unwatch: function () {
      if (media) {
        if (typeof media.removeEventListener === "function") {
          media.removeEventListener("change", onChange);
        }
        media = null;
      }
      globalThis.removeEventListener("resize", onChange);
      dotNetHelper = null;
    }
  };
})();
