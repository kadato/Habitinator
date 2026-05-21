// When the tab or WebView becomes visible again, reload the board (covers missed SignalR pushes).
window.HabitinatorBoardVisibility = (function () {
  let dotNetHelper = null;

  function onVisibilityChange() {
    if (!dotNetHelper || document.visibilityState !== "visible") {
      return;
    }
    dotNetHelper.invokeMethodAsync("OnBecameVisible").catch(function () {});
  }

  return {
    start: function (dotNetRef) {
      dotNetHelper = dotNetRef;
      document.addEventListener("visibilitychange", onVisibilityChange);
    },
    stop: function () {
      document.removeEventListener("visibilitychange", onVisibilityChange);
      dotNetHelper = null;
    }
  };
})();

window.HabitinatorKeyboardShortcuts = (function () {
  let dotNetHelper = null;

  function onKeyDown(e) {
    if (!dotNetHelper) return;

    const isUndo = (e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z';
    if (!isUndo) return;

    const activeElement = document.activeElement;
    if (activeElement) {
      const tagName = activeElement.tagName.toLowerCase();
      const isInput = tagName === 'input' || tagName === 'textarea' || activeElement.isContentEditable;
      if (isInput) {
        return;
      }
    }

    e.preventDefault();
    dotNetHelper.invokeMethodAsync("OnCtrlZPressed").catch(function () {});
  }

  return {
    start: function (dotNetRef) {
      dotNetHelper = dotNetRef;
      window.addEventListener("keydown", onKeyDown);
    },
    stop: function () {
      window.removeEventListener("keydown", onKeyDown);
      dotNetHelper = null;
    }
  };
})();

