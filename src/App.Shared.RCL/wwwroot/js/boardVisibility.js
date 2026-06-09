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
  let isAltHeld = false;
  let isShiftHeld = false;
  let lastKeyPressed = null;
  let lastKeyTime = 0;

  function updateShortcutOverlay() {
    const activeElement = document.activeElement;
    const isEditing = activeElement && (
      activeElement.tagName.toLowerCase() === 'input' || 
      activeElement.tagName.toLowerCase() === 'textarea' || 
      activeElement.isContentEditable
    );

    if ((isAltHeld || isShiftHeld) && !isEditing) {
      document.body.classList.add("hab-show-shortcuts");
    } else {
      document.body.classList.remove("hab-show-shortcuts");
    }
  }

  function onKeyDown(e) {
    if (e.key === 'Alt') {
      isAltHeld = true;
      updateShortcutOverlay();
    }
    if (e.key === 'Shift') {
      isShiftHeld = true;
      updateShortcutOverlay();
    }

    if (!dotNetHelper) return;

    // 1. Global shortcut: Ctrl+K / Cmd+K (Command Palette)
    const isCmdK = (e.ctrlKey || e.metaKey) && e.code === 'KeyK';
    if (isCmdK) {
      e.preventDefault();
      dotNetHelper.invokeMethodAsync("OnCtrlKPressed").catch(function () {});
      return;
    }

    // 2. Global shortcut: Ctrl+Z / Cmd+Z (Undo)
    const isUndo = (e.ctrlKey || e.metaKey) && e.code === 'KeyZ';

    // Helper to check if active element is input/editable
    const activeElement = document.activeElement;
    const isEditing = activeElement && (
      activeElement.tagName.toLowerCase() === 'input' || 
      activeElement.tagName.toLowerCase() === 'textarea' || 
      activeElement.isContentEditable
    );

    // If Escape is pressed while in an input field, blur it to exit editing mode
    if (e.code === 'Escape' || e.key === 'Escape') {
      if (isEditing && activeElement && !activeElement.closest('.mud-dialog')) {
        activeElement.blur();
        e.preventDefault();
        return;
      }
    }

    // If typing in an input field, do not trigger single-key or undo shortcuts
    if (isEditing) {
      return;
    }

    if (isUndo) {
      e.preventDefault();
      dotNetHelper.invokeMethodAsync("OnCtrlZPressed").catch(function () {});
      return;
    }

    // Navigation key sequences: g followed by b/s/p
    const now = Date.now();
    if (lastKeyPressed === 'g' && (now - lastKeyTime < 1000)) {
      if (e.code === 'KeyB') {
        e.preventDefault();
        dotNetHelper.invokeMethodAsync("NavigateTo", "/").catch(function () {});
        lastKeyPressed = null;
        return;
      }
      if (e.code === 'KeyS') {
        e.preventDefault();
        dotNetHelper.invokeMethodAsync("NavigateTo", "/stats").catch(function () {});
        lastKeyPressed = null;
        return;
      }
      if (e.code === 'KeyP') {
        e.preventDefault();
        dotNetHelper.invokeMethodAsync("NavigateTo", "/settings").catch(function () {});
        lastKeyPressed = null;
        return;
      }
    }

    if (e.code === 'KeyG') {
      lastKeyPressed = 'g';
      lastKeyTime = now;
    }

    // Single-key shortcuts (when not editing, allowing Shift/Alt modifiers)
    if (!e.ctrlKey && !e.metaKey) {
      if (e.code === 'KeyN') {
        e.preventDefault();
        dotNetHelper.invokeMethodAsync("OnNPressed").catch(function () {});
        return;
      }
      if (e.code === 'KeyS') {
        e.preventDefault();
        dotNetHelper.invokeMethodAsync("OnSPressed").catch(function () {});
        return;
      }
      if (e.code === 'Digit1') {
        e.preventDefault();
        dotNetHelper.invokeMethodAsync("OnDigitPressed", 1).catch(function () {});
        return;
      }
      if (e.code === 'Digit2') {
        e.preventDefault();
        dotNetHelper.invokeMethodAsync("OnDigitPressed", 2).catch(function () {});
        return;
      }
      if (e.code === 'Digit3') {
        e.preventDefault();
        dotNetHelper.invokeMethodAsync("OnDigitPressed", 3).catch(function () {});
        return;
      }
    }
  }

  function onKeyUp(e) {
    if (e.key === 'Alt') {
      isAltHeld = false;
      updateShortcutOverlay();
    }
    if (e.key === 'Shift') {
      isShiftHeld = false;
      updateShortcutOverlay();
    }
  }

  function onWindowBlur() {
    isAltHeld = false;
    isShiftHeld = false;
    updateShortcutOverlay();
  }

  return {
    start: function (dotNetRef) {
      dotNetHelper = dotNetRef;
      window.addEventListener("keydown", onKeyDown);
      window.addEventListener("keyup", onKeyUp);
      window.addEventListener("blur", onWindowBlur);
    },
    stop: function () {
      window.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("keyup", onKeyUp);
      window.removeEventListener("blur", onWindowBlur);
      document.body.classList.remove("hab-show-shortcuts");
      dotNetHelper = null;
      isAltHeld = false;
      isShiftHeld = false;
      lastKeyPressed = null;
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


