// Decides whether a newly opened dialog takes focus.
// On touch devices such as phones and MAUI WebView autofocus stays off.
// Autofocus would open the on-screen keyboard and push the bottom sheet up.
globalThis.HabitinatorDialogFocus = (function () {
  function isMaui() {
    return globalThis.habitinatorIsMaui === true;
  }

  function hasCoarsePointer() {
    try {
      return typeof globalThis.matchMedia === "function" &&
        globalThis.matchMedia("(pointer: coarse)").matches;
    } catch {
      return false;
    }
  }

  function shouldAutoFocus() {
    if (isMaui()) {
      return false;
    }
    if (hasCoarsePointer()) {
      return false;
    }
    return true;
  }

  return {
    shouldAutoFocus: shouldAutoFocus
  };
})();
