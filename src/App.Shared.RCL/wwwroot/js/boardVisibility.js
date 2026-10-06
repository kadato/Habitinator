// When the tab or WebView becomes visible again, reload the board. This covers missed SignalR pushes.
globalThis.HabitinatorBoardVisibility = (function () {
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

globalThis.HabitinatorKeyboardShortcuts = (function () {
  let layoutHelper = null;
  let boardHelper = null;
  let isListenersAdded = false;
  let isEnabled = true;

  function isEditing() {
    const activeElement = document.activeElement;
    return activeElement && (
      activeElement.tagName.toLowerCase() === 'input' || 
      activeElement.tagName.toLowerCase() === 'textarea' || 
      activeElement.isContentEditable ||
      activeElement.closest('.mud-input-slot') ||
      activeElement.closest('.mud-popover') ||
      activeElement.closest('.mud-list') ||
      activeElement.closest('.mud-menu')
    );
  }

  function isInteractiveElement(el) {
    if (!el) return false;
    const tagName = el.tagName.toLowerCase();
    if (['input', 'textarea', 'select', 'button', 'a'].includes(tagName)) {
      return true;
    }
    if (el.isContentEditable) {
      return true;
    }
    const role = el.getAttribute('role');
    if (role && ['button', 'checkbox', 'menu', 'menuitem', 'tab', 'option', 'listbox', 'slider', 'combobox', 'radio'].includes(role)) {
      return true;
    }
    if (el.closest('.mud-button-root') || el.closest('.mud-checkbox') || el.closest('.mud-switch') || el.closest('.mud-menu') || el.closest('.mud-list-item')) {
      return true;
    }
    return false;
  }

  function needsArrowKeys(el) {
    if (!el) return false;
    const tagName = el.tagName.toLowerCase();
    if (tagName === 'select') return true;
    if (tagName === 'input' && ['range', 'date', 'time', 'datetime-local', 'month', 'week'].includes(el.type)) return true;
    const role = el.getAttribute('role');
    if (role && ['listbox', 'slider', 'combobox'].includes(role)) return true;
    if (el.closest('.mud-select') || el.closest('.mud-slider')) return true;
    return false;
  }

  function isElementVisible(el) {
    if (el.offsetParent === null && globalThis.getComputedStyle(el).position !== 'fixed') {
      return false;
    }

    const rect = el.getBoundingClientRect();
    if (rect.width === 0 || rect.height === 0) return false;

    const style = globalThis.getComputedStyle(el);
    if (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0') return false;

    return (
      rect.top >= -rect.height &&
      rect.left >= -rect.width &&
      rect.top <= (globalThis.innerHeight || document.documentElement.clientHeight) &&
      rect.left <= (globalThis.innerWidth || document.documentElement.clientWidth)
    );
  }

  function isCommandPaletteOpen() {
    const palette = document.querySelector('.cmd-palette-dialog, .cmd-palette-backdrop');
    return !!(palette && isElementVisible(palette));
  }

  function isModalOpen() {
    const dialogs = document.querySelectorAll('.mud-dialog, .hab-modal, .mud-overlay-dialog');
    for (const d of dialogs) {
      if (d.closest('.cmd-palette-backdrop') || d.closest('.cmd-palette-dialog')) continue;
      if (isElementVisible(d)) {
        return true;
      }
    }
    return false;
  }

  function getActiveOpenContainer() {
    const dialogs = Array.from(document.querySelectorAll('.mud-dialog'));
    const popovers = Array.from(document.querySelectorAll('.mud-popover.mud-popover-open'));
    
    const activeContainers = [...dialogs, ...popovers].filter(isElementVisible);
    
    if (activeContainers.length === 0) {
      return null;
    }
    
    activeContainers.sort((a, b) => {
      if (a.contains(b)) return 1;
      if (b.contains(a)) return -1;
      return a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING ? 1 : -1;
    });
    
    return activeContainers.at(-1);
  }

  function getScrollableContainer(container) {
    if (!container) return globalThis;
    const known = container.querySelector('.edit-daily-body, .edit-habit-body, .archived-list, .daily-yesterday-body');
    if (known) return known;

    const dialogContent = container.querySelector('.mud-dialog-content');
    if (dialogContent) {
      const style = globalThis.getComputedStyle(dialogContent);
      if (style.overflowY === 'auto' || style.overflowY === 'scroll') {
        return dialogContent;
      }
    }

    const all = container.querySelectorAll('*');
    for (const element of all) {
      const style = globalThis.getComputedStyle(element);
      if (style.overflowY === 'auto' || style.overflowY === 'scroll') {
        return element;
      }
    }
    return container;
  }

  function scrollTargetElement(target, amount) {
    if (target === globalThis) {
      globalThis.scrollBy({ top: amount, behavior: 'smooth' });
    } else {
      target.scrollBy({ top: amount, behavior: 'smooth' });
    }
  }

  function scrollToPosition(target, position) {
    if (target === globalThis) {
      const scrollingEl = document.scrollingElement || document.documentElement || document.body;
      globalThis.scrollTo({ top: position === 'top' ? 0 : scrollingEl.scrollHeight, behavior: 'smooth' });
    } else {
      target.scrollTo({ top: position === 'top' ? 0 : target.scrollHeight, behavior: 'smooth' });
    }
  }

  function handleTagPickerEscape(e, activeElement) {
    if (activeElement?.classList.contains('habit-tag-picker__search')) {
      e.preventDefault();
      e.stopPropagation();
      const picker = activeElement.closest('.habit-tag-picker');
      const control = picker?.querySelector('.habit-tag-picker__control');
      if (control) {
        control.click();
        control.focus();
      }
      return true;
    }
    return false;
  }

  function handleTagsPopoverEscape(activeElement) {
    const tagsPopover = document.querySelector('.board-tags-menu-popover');
    if (tagsPopover && isElementVisible(tagsPopover)) {
      const activator = document.querySelector('.board-tags-menu-activator .board-trigger-btn');
      if (activator) {
        activator.click();
        activeElement?.blur();
        return true;
      }
    }
    return false;
  }

  function handleOpenPopoverEscape(activeElement) {
    const hasOpenPopover = Array.from(document.querySelectorAll('.mud-popover')).some(isElementVisible);
    if (hasOpenPopover) {
      activeElement?.blur();
      setTimeout(() => {
        const events = ['pointerdown', 'mousedown', 'mouseup', 'pointerup', 'click'];
        events.forEach(type => {
          document.body.dispatchEvent(new MouseEvent(type, { bubbles: true, cancelable: true }));
        });
        
        const newActive = document.activeElement;
        if (newActive && (
          newActive.classList.contains('mud-input-slot') || 
          newActive.closest('.mud-input-slot') ||
          newActive.closest('.mud-select') ||
          newActive.closest('.mud-input-control')
        )) {
          newActive.blur();
        }
      }, 50);
      return true;
    }
    return false;
  }

  function handleEditModeEscape(e, activeElement, isEdit) {
    if (isEdit && activeElement && !activeElement.closest('.mud-popover')) {
      if (activeElement.closest('.edit-daily-dialog, .edit-habit-dialog')) {
        if (!activeElement.classList.contains('habit-tag-picker__search')) {
          activeElement.blur();
          e.preventDefault();
          e.stopPropagation();
        }
      } else {
        activeElement.blur();
        e.preventDefault();
      }
      return true;
    }
    return false;
  }

  function handleEscapeKey(e, activeElement, isEdit) {
    if (activeElement?.closest('.timer-target-field')) {
      const hasOpenPopover = Array.from(document.querySelectorAll('.mud-popover')).some(isElementVisible);
      if (hasOpenPopover) {
        return;
      }
    }

    if (handleTagPickerEscape(e, activeElement)) return;
    if (handleTagsPopoverEscape(activeElement)) return;
    if (handleOpenPopoverEscape(activeElement)) return;
    handleEditModeEscape(e, activeElement, isEdit);
  }

  function handleScrolling(e, activeElement) {
    if (e.ctrlKey || e.metaKey || e.altKey) {
      return;
    }
    if ((e.key === ' ' || e.key === 'Space') && isInteractiveElement(activeElement)) {
      return;
    }
    if (['ArrowDown', 'ArrowUp'].includes(e.key) && needsArrowKeys(activeElement)) {
      return;
    }

    e.preventDefault();

    const activeContainer = getActiveOpenContainer();
    const target = activeContainer ? getScrollableContainer(activeContainer) : globalThis;

    const scrollSpeed = 100;
    const pageSpeed = target === globalThis ? globalThis.innerHeight * 0.8 : target.clientHeight * 0.8;

    if (e.key === 'ArrowDown' || e.key === 'j' || e.key === 'J') {
      scrollTargetElement(target, scrollSpeed);
    } else if (e.key === 'ArrowUp' || e.key === 'k' || e.key === 'K') {
      scrollTargetElement(target, -scrollSpeed);
    } else if (e.key === 'PageDown' || ((e.key === ' ' || e.key === 'Space') && !e.shiftKey)) {
      scrollTargetElement(target, pageSpeed);
    } else if (e.key === 'PageUp' || ((e.key === ' ' || e.key === 'Space') && e.shiftKey)) {
      scrollTargetElement(target, -pageSpeed);
    } else if (e.key === 'Home') {
      scrollToPosition(target, 'top');
    } else if (e.key === 'End') {
      scrollToPosition(target, 'bottom');
    }
  }

  function preventPopoverScroll(e) {
    if (document.querySelector('.mud-popover-open')) {
      if (e.key !== 'Escape' && e.key !== 'Esc') {
        const scrollKeys = ['ArrowDown', 'ArrowUp', 'Space', ' ', 'PageDown', 'PageUp', 'Home', 'End'];
        if (scrollKeys.includes(e.key)) {
          const activeElement = document.activeElement;
          const isInput = activeElement && (
            activeElement.tagName.toLowerCase() === 'input' || 
            activeElement.tagName.toLowerCase() === 'textarea' || 
            activeElement.isContentEditable
          );
          if (!(isInput && (e.key === ' ' || e.key === 'Space'))) {
            e.preventDefault();
          }
        }
        return true;
      }
    }
    return false;
  }

  function handleEditModeScrolling(e, activeElement) {
    if (activeElement && (activeElement.classList.contains('mud-input-slot') || activeElement.closest('.mud-popover') || activeElement.closest('.mud-list')) && ['ArrowUp', 'ArrowDown'].includes(e.key)) {
      e.preventDefault();
    }
  }

  function focusSearchInput(e) {
    const searchInput = document.querySelector('.board-search-field input, #board-search') ||
                        Array.from(document.querySelectorAll('input[placeholder*="Search" i]'))
                        .find(el => {
                          const ph = el.placeholder.toLowerCase();
                          return !ph.includes("session") && !ph.includes("type a custom");
                        });
    if (searchInput) {
      e.preventDefault();
      searchInput.focus();
      if (typeof searchInput.select === 'function') {
        searchInput.select();
      }
      return true;
    }
    return false;
  }

  function focusAddInput(e) {
    const addInput = document.querySelector('.board-column--habit .board-add-wrap input, .board-column--todo .board-add-wrap input, input[placeholder*="Add" i]');
    if (addInput) {
      e.preventDefault();
      addInput.focus();
      return true;
    }
    return false;
  }

  function switchBoardTab(e) {
    const idx = Number.parseInt(e.key, 10) - 1;
    const tabBtn = document.getElementById('board-tab-' + idx);
    if (tabBtn) {
      e.preventDefault();
      tabBtn.click();
      return true;
    }
    return false;
  }

  let pendingChord = null;
  let chordTimeout = null;

  const chordActions = {
    g: { b: "nav-board", u: "nav-upcoming", s: "nav-stats", p: "nav-settings" },
    c: { h: "create-habit", d: "create-daily", t: "create-todo" }
  };

  const singleKeyActions = { h: "create-habit", d: "create-daily", t: "create-todo", s: "toggle-timer" };

  function invokeShortcutAction(action) {
    const helper = layoutHelper || boardHelper;
    if (helper) {
      helper.invokeMethodAsync("OnShortcutAction", action).catch(function () {});
    }
  }

  function handlePendingChord(e, key) {
    const prefix = pendingChord;
    clearTimeout(chordTimeout);
    pendingChord = null;
    const actions = chordActions[prefix];
    const action = actions ? actions[key] : null;
    if (action) {
      e.preventDefault();
      invokeShortcutAction(action);
      return true;
    }
    return false;
  }

  function startChord(prefix, timeoutMs, onTimeout) {
    pendingChord = prefix;
    chordTimeout = setTimeout(function () {
      if (pendingChord === prefix) {
        pendingChord = null;
        if (onTimeout) {
          onTimeout();
        }
      }
    }, timeoutMs);
  }

  function handleSingleKey(e, key) {
    if (key === 'n') {
      return focusAddInput(e);
    }
    const action = singleKeyActions[key];
    if (action) {
      e.preventDefault();
      invokeShortcutAction(action);
      return true;
    }
    if (key === '/') {
      return focusSearchInput(e);
    }
    if (key === '1' || key === '2' || key === '3') {
      return switchBoardTab(e);
    }
    return false;
  }

  function handleDirectShortcuts(e) {
    if (e.ctrlKey || e.metaKey || e.altKey) {
      return false;
    }

    const key = e.key.toLowerCase();

    // Handle second key of two-key sequences (e.g. 'g b', 'c h')
    if (pendingChord && handlePendingChord(e, key)) {
      return true;
    }

    // Start chord: 'g' prefix for navigation shortcuts
    if (key === 'g') {
      startChord('g', 1200);
      return true;
    }

    // Start chord: 'c' prefix for create shortcuts, or focus the add input
    if (key === 'c') {
      startChord('c', 350, function () { focusAddInput(e); });
      return true;
    }

    return handleSingleKey(e, key);
  }

  function matchesKey(e, code, keyLower) {
    return e.code === code || e.key === keyLower || e.key === keyLower.toUpperCase();
  }

  function handleCmdK(e, helper, modalOpen) {
    const isCmdK = (e.ctrlKey || e.metaKey) && matchesKey(e, 'KeyK', 'k');
    if (!isCmdK) {
      return false;
    }
    if (!modalOpen) {
      e.preventDefault();
      e.stopPropagation();
      if (helper) {
        helper.invokeMethodAsync("OnCmdKPressed").catch(function () {});
      }
    }
    return true;
  }

  function handleCtrlShortcut(e, helper, modalOpen, code, keyLower, action) {
    const isMatch = (e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && matchesKey(e, code, keyLower);
    if (!isMatch) {
      return false;
    }
    e.preventDefault();
    e.stopPropagation();
    if (!modalOpen && helper) {
      helper.invokeMethodAsync("OnShortcutAction", action).catch(function () {});
    }
    return true;
  }

  function handleAltT(e, helper, modalOpen) {
    const isAltT = e.altKey && !e.ctrlKey && !e.metaKey && matchesKey(e, 'KeyT', 't');
    if (!isAltT) {
      return false;
    }
    e.preventDefault();
    e.stopPropagation();
    if (!modalOpen && helper) {
      helper.invokeMethodAsync("OnShortcutAction", "create-todo").catch(function () {});
    }
    return true;
  }

  function handleUndo(e, modalOpen) {
    const isUndo = (e.ctrlKey || e.metaKey) && !e.shiftKey && matchesKey(e, 'KeyZ', 'z');
    if (!isUndo) {
      return false;
    }
    if (isEditing() || modalOpen) {
      return true;
    }
    e.preventDefault();
    e.stopPropagation();
    const undoHelper = boardHelper || layoutHelper;
    if (undoHelper) {
      undoHelper.invokeMethodAsync("OnCtrlZPressed").catch(function () {});
    }
    return true;
  }

  function handleEscapeOrModalKeys(e, helper, modalOpen, activeElement, isEdit) {
    if (e.code === 'Escape' || e.key === 'Escape') {
      if (isCommandPaletteOpen()) {
        e.preventDefault();
        e.stopPropagation();
        if (helper) {
          helper.invokeMethodAsync("OnEscapePressed").catch(function () {});
        }
        return true;
      }
      handleEscapeKey(e, activeElement, isEdit);
      return true;
    }

    if (isCommandPaletteOpen()) {
      return true;
    }

    if (modalOpen) {
      // While a modal is open, board-level shortcuts and item creations must not fire.
      const scrollKeys = ['ArrowDown', 'ArrowUp', 'Space', ' ', 'PageDown', 'PageUp', 'Home', 'End'];
      if (!isEdit && scrollKeys.includes(e.key)) {
        handleScrolling(e, activeElement);
      }
      return true;
    }
    return false;
  }

  function onKeyDown(e) {
    const helper = layoutHelper || boardHelper;
    const modalOpen = isModalOpen();

    // 1. Command palette toggle: Ctrl+K / Cmd+K
    if (handleCmdK(e, helper, modalOpen)) {
      return;
    }

    // 2. Intercept Ctrl+H / Cmd+H before the browser opens history
    if (handleCtrlShortcut(e, helper, modalOpen, 'KeyH', 'h', "create-habit")) {
      return;
    }

    // 3. Intercept Ctrl+D / Cmd+D before the browser bookmarks the page
    if (handleCtrlShortcut(e, helper, modalOpen, 'KeyD', 'd', "create-daily")) {
      return;
    }

    // 4. Alt+T for New To-do
    if (handleAltT(e, helper, modalOpen)) {
      return;
    }

    // 5. Global shortcut: Ctrl+Z / Cmd+Z, undo
    if (handleUndo(e, modalOpen)) {
      return;
    }

    if (preventPopoverScroll(e)) return;

    const activeElement = document.activeElement;
    const isEdit = isEditing();

    if (handleEscapeOrModalKeys(e, helper, modalOpen, activeElement, isEdit)) {
      return;
    }

    if (!helper) return;

    if (isEdit) {
      handleEditModeScrolling(e, activeElement);
      return;
    }

    // Direct power-user keyboard shortcuts when not typing in an input
    if (handleDirectShortcuts(e)) return;

    // Keyboard scrolling when not editing
    const scrollKeys = ['ArrowDown', 'ArrowUp', 'Space', ' ', 'PageDown', 'PageUp', 'Home', 'End', 'j', 'J', 'k', 'K'];
    if (scrollKeys.includes(e.key)) {
      handleScrolling(e, activeElement);
    }
  }

  function ensureListeners() {
    if (!isEnabled) return;
    if (isListenersAdded) return;
    globalThis.addEventListener("keydown", onKeyDown, true);
    isListenersAdded = true;
  }

  function removeListeners() {
    if (!isListenersAdded) return;
    globalThis.removeEventListener("keydown", onKeyDown, true);
    isListenersAdded = false;
  }

  return {
    setEnabled: function (enabled) {
      isEnabled = !!enabled;
      if (isEnabled) {
        if (layoutHelper || boardHelper) {
          ensureListeners();
        }
      } else {
        removeListeners();
      }
    },
    startGlobal: function (dotNetRef) {
      layoutHelper = dotNetRef;
      ensureListeners();
    },
    startBoard: function (dotNetRef) {
      boardHelper = dotNetRef;
      ensureListeners();
    },
    stopBoard: function () {
      boardHelper = null;
      if (!layoutHelper && !boardHelper) {
        removeListeners();
      }
    },
    start: function (dotNetRef) {
      boardHelper = dotNetRef;
      ensureListeners();
    },
    stop: function () {
      boardHelper = null;
      if (!layoutHelper && !boardHelper) {
        removeListeners();
      }
    }
  };
})();
