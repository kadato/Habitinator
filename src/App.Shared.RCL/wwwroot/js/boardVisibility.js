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

    // 1. Global shortcut: Ctrl+K / Cmd+K (Command Palette)
    const isCmdK = (e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k';
    if (isCmdK) {
      e.preventDefault();
      dotNetHelper.invokeMethodAsync("OnCtrlKPressed").catch(function () {});
      return;
    }

    // 2. Global shortcut: Ctrl+Z / Cmd+Z (Undo)
    const isUndo = (e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z';

    // Helper to check if active element is input/editable
    const activeElement = document.activeElement;
    const isEditing = activeElement && (
      activeElement.tagName.toLowerCase() === 'input' || 
      activeElement.tagName.toLowerCase() === 'textarea' || 
      activeElement.isContentEditable
    );

    // If typing in an input field, do not trigger single-key or undo shortcuts
    if (isEditing) {
      return;
    }

    if (isUndo) {
      e.preventDefault();
      dotNetHelper.invokeMethodAsync("OnCtrlZPressed").catch(function () {});
      return;
    }

    // Single-key shortcuts (when not editing)
    if (!e.ctrlKey && !e.metaKey && !e.altKey) {
      const key = e.key.toLowerCase();
      if (key === 'n') {
        e.preventDefault();
        dotNetHelper.invokeMethodAsync("OnNPressed").catch(function () {});
        return;
      }
      if (key === 's') {
        e.preventDefault();
        dotNetHelper.invokeMethodAsync("OnSPressed").catch(function () {});
        return;
      }
      if (key === '1' || key === '2' || key === '3') {
        e.preventDefault();
        const digit = parseInt(key, 10);
        dotNetHelper.invokeMethodAsync("OnDigitPressed", digit).catch(function () {});
        return;
      }
    }
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

window.HabitinatorCommandPalette = {
  scrollSelectedIntoView: function () {
    const selected = document.querySelector('.hab-command-palette-item--selected');
    if (selected) {
      selected.scrollIntoView({ block: 'nearest' });
    }
  }
};


