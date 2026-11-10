// Single-render viewport source for the board. MainBoard renders either the mobile
// tabbed column or the desktop three-column grid, never both, so hidden columns
// never mount, never init SortableJS, and never double-handle events.
globalThis.HabitinatorBoardViewport = (function () {
  var query = "(max-width: 959.98px)";
  var phoneQuery = "(max-width: 767.98px)";
  var media = null;
  var phoneMedia = null;
  var dotNetHelper = null;
  // Second subscriber slot. MainBoard owns `watch`; overlays such as the AI
  // assistant use `watchPhoneSheet`. Both get phone-breakpoint updates.
  var sheetHelper = null;
  var listening = false;

  function current() {
    if (typeof globalThis.matchMedia === "function") {
      return globalThis.matchMedia(query).matches;
    }
    return (globalThis.innerWidth || 1024) < 960;
  }

  function isPhone() {
    if (typeof globalThis.matchMedia === "function") {
      return globalThis.matchMedia(phoneQuery).matches;
    }
    return (globalThis.innerWidth || 1024) < 768;
  }

  function onChange() {
    if (dotNetHelper) {
      dotNetHelper.invokeMethodAsync("OnViewportChanged", current()).catch(function () {});
    }
  }

  function onPhoneChange() {
    notifyPhoneHelper(dotNetHelper);
    notifyPhoneHelper(sheetHelper);
  }

  function notifyPhoneHelper(helper) {
    if (helper) {
      try {
        helper.invokeMethodAsync("OnPhoneViewportChanged", isPhone()).catch(function () {});
      } catch (e) { /* helper went away; unwatch clears it */ }
    }
  }

  function ensureListening() {
    if (listening) {
      return;
    }
    listening = true;
    if (typeof globalThis.matchMedia === "function") {
      media = globalThis.matchMedia(query);
      if (media && typeof media.addEventListener === "function") {
        media.addEventListener("change", onChange);
      } else {
        globalThis.addEventListener("resize", onChange);
      }
      phoneMedia = globalThis.matchMedia(phoneQuery);
      if (phoneMedia && typeof phoneMedia.addEventListener === "function") {
        phoneMedia.addEventListener("change", onPhoneChange);
      } else {
        globalThis.addEventListener("resize", onPhoneChange);
      }
    } else {
      globalThis.addEventListener("resize", onChange);
      globalThis.addEventListener("resize", onPhoneChange);
    }
  }

  function stopListening() {
    if (media) {
      if (typeof media.removeEventListener === "function") {
        media.removeEventListener("change", onChange);
      }
      media = null;
    }
    if (phoneMedia) {
      if (typeof phoneMedia.removeEventListener === "function") {
        phoneMedia.removeEventListener("change", onPhoneChange);
      }
      phoneMedia = null;
    }
    globalThis.removeEventListener("resize", onChange);
    globalThis.removeEventListener("resize", onPhoneChange);
    listening = false;
  }

  return {
    isMobile: function () {
      return current();
    },
    isPhone: function () {
      return isPhone();
    },
    watch: function (dotNetRef) {
      dotNetHelper = dotNetRef;
      ensureListening();
    },
    watchPhoneSheet: function (dotNetRef) {
      sheetHelper = dotNetRef;
      ensureListening();
    },
    unwatch: function () {
      dotNetHelper = null;
      if (!sheetHelper) {
        stopListening();
      }
    },
    unwatchPhoneSheet: function () {
      sheetHelper = null;
      if (!dotNetHelper) {
        stopListening();
      }
    }
  };
})();
