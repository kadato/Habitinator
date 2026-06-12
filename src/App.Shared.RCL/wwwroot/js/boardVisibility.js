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
  let layoutHelper = null;
  let boardHelper = null;
  let isListenersAdded = false;

  let isAltHeld = false;
  let isShiftHeld = false;


  // Shift Shortcut Overlay State
  let shortcutModeActive = false;
  let shiftLock = false;
  let lastShiftPressTime = 0;
  let currentSequence = "";
  let targets = [];
  let overlayContainer = null;
  let hudElement = null;

  const availableKeys = ['A', 'C', 'E', 'I', 'J', 'K', 'L', 'M', 'N', 'Q', 'U', 'V', 'W', 'X', 'Y', 'Z'];



  function updateShortcutOverlay() {
    if (isEditing()) {
      document.body.classList.remove("hab-show-shortcuts");
      return;
    }

    if (isAltHeld || isShiftHeld || shortcutModeActive) {
      if (getActiveOpenContainer()) {
        document.body.classList.remove("hab-show-shortcuts");
        return;
      }
      document.body.classList.add("hab-show-shortcuts");
    } else {
      document.body.classList.remove("hab-show-shortcuts");
    }
  }

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

  function getScrollableContainer(container) {
    if (!container) return window;
    const known = container.querySelector('.edit-daily-body, .edit-habit-body, .archived-list, .daily-yesterday-body');
    if (known) return known;

    const dialogContent = container.querySelector('.mud-dialog-content');
    if (dialogContent) {
      const style = window.getComputedStyle(dialogContent);
      if (style.overflowY === 'auto' || style.overflowY === 'scroll') {
        return dialogContent;
      }
    }

    const all = container.querySelectorAll('*');
    for (let i = 0; i < all.length; i++) {
      const style = window.getComputedStyle(all[i]);
      if (style.overflowY === 'auto' || style.overflowY === 'scroll') {
        return all[i];
      }
    }
    return container;
  }

  function scrollTargetElement(target, amount) {
    if (target === window) {
      window.scrollBy({ top: amount, behavior: 'smooth' });
    } else {
      target.scrollBy({ top: amount, behavior: 'smooth' });
    }
  }

  function scrollToPosition(target, position) {
    if (target === window) {
      const scrollingEl = document.scrollingElement || document.documentElement || document.body;
      window.scrollTo({ top: position === 'top' ? 0 : scrollingEl.scrollHeight, behavior: 'smooth' });
    } else {
      target.scrollTo({ top: position === 'top' ? 0 : target.scrollHeight, behavior: 'smooth' });
    }
  }

  function isInsideInputControl(el) {
    return el.closest('.mud-input-control') || 
           el.closest('.mud-input') || 
           el.closest('.board-search-wrapper') || 
           el.closest('.board-add-wrap') ||
           el.closest('.mud-input-adornment') ||
           el.closest('.timer-target-field') ||
           el.closest('.timer-focus-field');
  }

  function isInsideToggle(el) {
    if (!el.parentElement) return false;
    return el.parentElement.closest('.mud-checkbox, .mud-switch, .board-subtask-cb') !== null;
  }

  function isElementVisible(el) {
    // Check if element or any parent is display: none (skip fixed position elements which can have null offsetParent)
    if (el.offsetParent === null && window.getComputedStyle(el).position !== 'fixed') {
      return false;
    }

    const rect = el.getBoundingClientRect();
    if (rect.width === 0 || rect.height === 0) return false;

    const style = window.getComputedStyle(el);
    if (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0') return false;

    // Check if it's within viewport bounds
    const inViewport = (
      rect.top >= -rect.height &&
      rect.left >= -rect.width &&
      rect.top <= (window.innerHeight || document.documentElement.clientHeight) &&
      rect.left <= (window.innerWidth || document.documentElement.clientWidth)
    );
    return inViewport;
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
    
    return activeContainers[activeContainers.length - 1];
  }

  function buildShortcutTargets() {
    targets = [];
    const assignedElements = new Set();

    function addTarget(el, shortcut, type) {
      if (!el || assignedElements.has(el)) return;

      const rect = el.getBoundingClientRect();
      const isRendered = rect.width > 0 && rect.height > 0;
      if (!isRendered) return;

      const isFixed = ["B", "T", "P", "F", "H", "D", "O", "G", "R"].includes(shortcut);
      if (!isFixed && !isElementVisible(el)) return;

      assignedElements.add(el);
      targets.push({ element: el, shortcut, type });
    }

    const activeContainer = getActiveOpenContainer();

    if (activeContainer) {
      const inputs = Array.from(activeContainer.querySelectorAll('input:not([type="hidden"]):not([type="submit"]):not([type="button"]):not([disabled]), textarea:not([disabled]), select:not([disabled]), [contenteditable="true"], .mud-input-slot:not([disabled])'))
        .filter(el => !assignedElements.has(el) && isElementVisible(el) && !isInsideToggle(el));

      const toggles = Array.from(activeContainer.querySelectorAll('.mud-checkbox, .board-subtask-cb, .mud-switch'))
        .filter(el => !assignedElements.has(el) && isElementVisible(el) && !isInsideToggle(el));

      const clickables = Array.from(activeContainer.querySelectorAll('a[href]:not([href="#"]), button:not([disabled]):not(.stats-heatmap-day-btn), [role="button"]:not([disabled]), .mud-button-root:not([disabled]), .app-header-profile-btn, .app-header-username-btn, .board-card__title, .board-card__delete, [role="menuitem"]:not([disabled]):not(.mud-list-item-disabled):not([aria-disabled="true"]), [role="tab"]:not([disabled]), .mud-tab:not([disabled]), .mud-list-item-clickable:not([disabled]):not(.mud-list-item-disabled):not([aria-disabled="true"]), .mud-menu-item:not([disabled]):not(.mud-list-item-disabled):not([aria-disabled="true"]), .mud-expand-panel-header'))
        .filter(el => !assignedElements.has(el) && isElementVisible(el) && !isInsideInputControl(el) && !isInsideToggle(el));

      const totalDynamicElements = inputs.length + toggles.length + clickables.length;
      const useTwoChars = totalDynamicElements > availableKeys.length;
      let dynamicIndex = 0;

      function getDynamicKey(index) {
        if (!useTwoChars) {
          return availableKeys[index];
        }
        const firstIdx = Math.floor(index / availableKeys.length);
        const secondIdx = index % availableKeys.length;
        if (firstIdx < availableKeys.length) {
          return availableKeys[firstIdx] + availableKeys[secondIdx];
        }
        return 'X' + index;
      }

      inputs.forEach(el => {
        const key = getDynamicKey(dynamicIndex++);
        addTarget(el, key, "input");
      });

      toggles.forEach(el => {
        const key = getDynamicKey(dynamicIndex++);
        addTarget(el, key, "input");
      });

      clickables.forEach(el => {
        const key = getDynamicKey(dynamicIndex++);
        addTarget(el, key, "nav");
      });

      return;
    }

    // 1. Navigation Elements (fixed shortcuts)
    const allLinks = Array.from(document.querySelectorAll('a[href], button, [role="button"], .mud-button-root, .app-mobile-nav__item, .app-bottom-nav__item, .app-header-nav-btn'));
    
    const boardEl = allLinks.find(el => {
      const txt = el.textContent.trim().toLowerCase();
      const href = el.getAttribute('href') || '';
      return href === '/' || txt === 'board' || (el.classList.contains('app-mobile-nav__item') && txt.includes('board'));
    });
    addTarget(boardEl, "B", "nav");

    const statsEl = allLinks.find(el => {
      const txt = el.textContent.trim().toLowerCase();
      const href = el.getAttribute('href') || '';
      return href.includes('stats') || txt === 'stats' || txt === 'statistics';
    });
    addTarget(statsEl, "T", "nav");

    const settingsEl = allLinks.find(el => {
      const txt = el.textContent.trim().toLowerCase();
      const href = el.getAttribute('href') || '';
      return href.includes('settings') || txt === 'settings' || txt === 'preferences';
    });
    addTarget(settingsEl, "P", "nav");



    // 2. Common Inputs (fixed shortcuts)
    const searchInput = document.querySelector('.board-search-field input, #board-search') ||
                        Array.from(document.querySelectorAll('input[placeholder*="Search" i]'))
                        .find(el => {
                          const ph = el.placeholder.toLowerCase();
                          return !ph.includes("session") && !ph.includes("type a custom");
                        });
    addTarget(searchInput, "F", "input");

    const habitInput = document.querySelector('.board-column--habit .board-add-wrap input, input[placeholder*="Add habit" i]');
    addTarget(habitInput, "H", "input");

    const dailyInput = document.querySelector('.board-column--daily .board-add-wrap input, input[placeholder*="Add daily" i]');
    addTarget(dailyInput, "D", "input");

    const todoInput = document.querySelector('.board-column--todo .board-add-wrap input, input[placeholder*="Add to-do" i]');
    addTarget(todoInput, "O", "input");

    // 2b. Statistics Filters (fixed shortcuts)
    const statsTagSelect = document.querySelector('.stats-tag-select-sidebar .mud-input-slot');
    addTarget(statsTagSelect, "G", "input");

    const statsPeriodSelect = document.querySelector('.stats-year-select-sidebar .mud-input-slot');
    addTarget(statsPeriodSelect, "R", "input");

    // 3. Dynamic elements allocation (prefix-free)
    const inputs = Array.from(document.querySelectorAll('input:not([type="hidden"]):not([type="submit"]):not([type="button"]):not([disabled]), textarea:not([disabled]), select:not([disabled]), [contenteditable="true"], .mud-input-slot:not([disabled])'))
      .filter(el => !assignedElements.has(el) && isElementVisible(el) && !isInsideToggle(el));

    const toggles = Array.from(document.querySelectorAll('.mud-checkbox, .board-subtask-cb, .mud-switch'))
      .filter(el => !assignedElements.has(el) && isElementVisible(el) && !isInsideToggle(el));

    const clickables = Array.from(document.querySelectorAll('a[href]:not([href="#"]), button:not([disabled]):not(.stats-heatmap-day-btn), [role="button"]:not([disabled]), .mud-button-root:not([disabled]), .app-header-profile-btn, .app-header-username-btn, .board-card__title, .board-card__delete, [role="menuitem"]:not([disabled]):not(.mud-list-item-disabled):not([aria-disabled="true"]), [role="tab"]:not([disabled]), .mud-tab:not([disabled]), .mud-list-item-clickable:not([disabled]):not(.mud-list-item-disabled):not([aria-disabled="true"]), .mud-menu-item:not([disabled]):not(.mud-list-item-disabled):not([aria-disabled="true"]), .mud-expand-panel-header'))
      .filter(el => !assignedElements.has(el) && isElementVisible(el) && !isInsideInputControl(el) && !isInsideToggle(el));

    const totalDynamicElements = inputs.length + toggles.length + clickables.length;
    const useTwoChars = totalDynamicElements > availableKeys.length;
    let dynamicIndex = 0;

    function getDynamicKey(index) {
      if (!useTwoChars) {
        return availableKeys[index];
      }
      const firstIdx = Math.floor(index / availableKeys.length);
      const secondIdx = index % availableKeys.length;
      if (firstIdx < availableKeys.length) {
        return availableKeys[firstIdx] + availableKeys[secondIdx];
      }
      return 'X' + index;
    }

    inputs.forEach(el => {
      const key = getDynamicKey(dynamicIndex++);
      addTarget(el, key, "input");
    });

    toggles.forEach(el => {
      const key = getDynamicKey(dynamicIndex++);
      addTarget(el, key, "input");
    });

    clickables.forEach(el => {
      const key = getDynamicKey(dynamicIndex++);
      addTarget(el, key, "nav");
    });
  }

  function activateShortcutMode() {
    if (shortcutModeActive || isEditing()) return;
    shortcutModeActive = true;
    currentSequence = "";

    const activeContainer = getActiveOpenContainer();
    if (activeContainer) {
      document.body.classList.add("hab-shortcuts-modal-open");
    } else {
      document.body.classList.remove("hab-shortcuts-modal-open");
    }

    buildShortcutTargets();
    renderOverlays();
    showHUD();
  }

  function deactivateShortcutMode() {
    if (!shortcutModeActive) return;
    shortcutModeActive = false;
    shiftLock = false;
    currentSequence = "";

    if (overlayContainer) {
      overlayContainer.remove();
      overlayContainer = null;
    }

    if (hudElement) {
      hudElement.remove();
      hudElement = null;
    }

    targets = [];
    document.body.classList.remove("hab-show-shortcuts");
    document.body.classList.remove("hab-shortcuts-modal-open");
  }

  function renderOverlays() {
    if (overlayContainer) overlayContainer.remove();
    overlayContainer = document.createElement('div');
    overlayContainer.id = 'hab-shortcut-overlays';
    document.body.appendChild(overlayContainer);

    targets.forEach(t => {
      const rect = t.element.getBoundingClientRect();
      const badge = document.createElement('div');
      badge.className = `hab-shortcut-hint hab-shortcut-hint--${t.type}`;
      
      // Position badge
      const isInputEl = t.type === 'input';
      const isTagPicker = t.element.classList.contains('habit-tag-picker__control');
      const top = rect.top + window.scrollY + (rect.height / 2);
      let left;
      if (isTagPicker) {
        left = rect.right + window.scrollX - 24;
      } else {
        left = isInputEl 
          ? rect.left + window.scrollX + 16 
          : rect.left + window.scrollX + (rect.width / 2);
      }

      badge.style.top = `${top}px`;
      badge.style.left = `${left}px`;
      badge.dataset.shortcut = t.shortcut;

      // Render shortcut characters
      let html = '';
      for (let i = 0; i < t.shortcut.length; i++) {
        html += `<span class="key-char key-char--untyped">${t.shortcut[i]}</span>`;
      }
      badge.innerHTML = html;

      overlayContainer.appendChild(badge);

      // Trigger animation
      setTimeout(() => badge.classList.add('hab-shortcut-hint--visible'), 10);
    });
  }

  function showHUD() {
    if (hudElement) hudElement.remove();
    hudElement = document.createElement('div');
    hudElement.className = 'hab-shortcut-hud';
    
    const dot = document.createElement('div');
    dot.className = 'hab-shortcut-hud-dot';
    hudElement.appendChild(dot);

    const text = document.createElement('span');
    text.className = 'hud-text';
    text.textContent = shiftLock ? 'Shortcut Mode [Locked]' : 'Shortcut Mode Active';
    hudElement.appendChild(text);

    const keyBadge = document.createElement('div');
    keyBadge.className = 'hab-shortcut-hud-badge';
    keyBadge.style.display = 'none';
    hudElement.appendChild(keyBadge);

    document.body.appendChild(hudElement);

    setTimeout(() => hudElement.classList.add('hab-shortcut-hud--visible'), 10);
  }

  function updateHUD(seq) {
    if (!hudElement) return;
    const keyBadge = hudElement.querySelector('.hab-shortcut-hud-badge');
    const dot = hudElement.querySelector('.hab-shortcut-hud-dot');
    
    if (seq) {
      keyBadge.style.display = 'inline-flex';
      keyBadge.textContent = seq;
      dot.classList.add('hab-shortcut-hud-dot--warning');
    } else {
      keyBadge.style.display = 'none';
      dot.classList.remove('hab-shortcut-hud-dot--warning');
    }
  }

  function handleKeystrokeInShortcutMode(e) {
    const key = e.key;
    if (key === 'Escape' || key === 'Esc') {
      e.preventDefault();
      e.stopPropagation();
      shiftLock = false;
      deactivateShortcutMode();
      return;
    }

    if (e.ctrlKey || e.metaKey || e.altKey) return;
    if (key === 'Shift') return;

    const char = key.toUpperCase();
    if (!/^[A-Z0-9]$/.test(char)) return;

    e.preventDefault();
    e.stopPropagation();

    const newSeq = currentSequence + char;
    const matching = targets.filter(t => t.shortcut.startsWith(newSeq));

    if (matching.length === 0) {
      // No matches, play a brief warning on HUD and keep the previous sequence
      if (hudElement) {
        const text = hudElement.querySelector('.hud-text');
        const oldText = text.textContent;
        text.textContent = `No match for: ${char}`;
        text.style.color = '#ef4444';
        setTimeout(() => {
          text.textContent = oldText;
          text.style.color = '';
        }, 800);
      }
      return;
    }

    currentSequence = newSeq;
    updateHUD(currentSequence);

    // Update badges
    const badges = Array.from(overlayContainer.querySelectorAll('.hab-shortcut-hint'));
    badges.forEach(badge => {
      const shortcut = badge.dataset.shortcut;
      if (shortcut.startsWith(currentSequence)) {
        badge.classList.remove('hab-shortcut-hint--dimmed');
        
        let html = '';
        for (let i = 0; i < shortcut.length; i++) {
          if (i < currentSequence.length) {
            html += `<span class="key-char key-char--typed">${shortcut[i]}</span>`;
          } else {
            html += `<span class="key-char key-char--untyped">${shortcut[i]}</span>`;
          }
        }
        badge.innerHTML = html;
      } else {
        badge.classList.add('hab-shortcut-hint--dimmed');
      }
    });

    // If exactly one match, trigger it!
    if (matching.length === 1) {
      const matched = matching[0];
      const badge = Array.from(overlayContainer.querySelectorAll('.hab-shortcut-hint')).find(b => b.dataset.shortcut === matched.shortcut);
      if (badge) {
        badge.classList.add('hab-shortcut-hint--matched');
      }

      setTimeout(() => {
        executeTarget(matched);
        shiftLock = false;
        deactivateShortcutMode();
      }, 150);
    }
  }

  function executeTarget(target) {
    const el = target.element;
    if (target.type === 'input') {
      if (el.classList.contains('mud-input-slot')) {
        el.focus();
        el.click();
      } else if (el.classList.contains('mud-checkbox') || el.classList.contains('board-subtask-cb') || el.classList.contains('mud-switch')) {
        const inputChild = el.querySelector('input');
        if (inputChild) {
          inputChild.click();
        } else {
          el.click();
        }
      } else {
        el.focus();
        if (typeof el.select === 'function' && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA')) {
          el.select();
        }
      }
    } else {
      // For links and buttons, click them. If it is a Blazor SPA link, this correctly handles routing client-side.
      el.click();
    }
  }

  function onKeyDown(e) {
    // If a select dropdown or popover is open, do not intercept scroll keys or keyboard inputs,
    // but prevent browser from scrolling when Arrow/Space/Page/Home/End keys are pressed.
    if (!shortcutModeActive && document.querySelector('.mud-popover-open')) {
      if (e.key === 'Escape' || e.key === 'Esc') {
        // Let Escape pass through to the escape/blur handler below
      } else {
        const scrollKeys = ['ArrowDown', 'ArrowUp', 'Space', ' ', 'PageDown', 'PageUp', 'Home', 'End'];
        if (scrollKeys.includes(e.key)) {
          const activeElement = document.activeElement;
          const isInput = activeElement && (
            activeElement.tagName.toLowerCase() === 'input' || 
            activeElement.tagName.toLowerCase() === 'textarea' || 
            activeElement.isContentEditable
          );
          if (isInput && (e.key === ' ' || e.key === 'Space')) {
            // Let space be typed normally in inputs
          } else {
            e.preventDefault();
          }
        }
        return;
      }
    }

    // Escape blur functionality (from original codebase)
    const activeElement = document.activeElement;
    const isEdit = isEditing();
    
    if (e.code === 'Escape' || e.key === 'Escape') {
      if (shortcutModeActive) {
        shiftLock = false;
        deactivateShortcutMode();
      }

      if (activeElement && activeElement.closest('.timer-target-field')) {
        const openPopover = Array.from(document.querySelectorAll('.mud-popover')).find(isElementVisible);
        if (openPopover) {
          return;
        }
      }

      if (activeElement && activeElement.classList.contains('habit-tag-picker__search')) {
        e.preventDefault();
        e.stopPropagation();
        const picker = activeElement.closest('.habit-tag-picker');
        if (picker) {
          const control = picker.querySelector('.habit-tag-picker__control');
          if (control) {
            control.click();
            control.focus();
          }
        }
        return;
      }
      
      const tagsPopover = document.querySelector('.board-tags-menu-popover');
      if (tagsPopover && isElementVisible(tagsPopover)) {
        const activator = document.querySelector('.board-tags-trigger');
        if (activator) {
          activator.click();
          if (activeElement) {
            activeElement.blur();
          }
          return;
        }
      }
      
      const openPopover = Array.from(document.querySelectorAll('.mud-popover')).find(isElementVisible);
      if (openPopover) {
        if (activeElement) {
          activeElement.blur();
        }
        setTimeout(() => {
          const events = ['pointerdown', 'mousedown', 'mouseup', 'pointerup', 'click'];
          events.forEach(type => {
            document.body.dispatchEvent(new MouseEvent(type, { bubbles: true, cancelable: true }));
          });
          
          // Blur any select slot or input control that got focused back by Blazor's popover closing logic
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
        return;
      }
      
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
        return;
      }
    }

    // Double-tap Shift toggling
    if (e.key === 'Shift') {
      if (!e.repeat) {
        const now = Date.now();
        if (now - lastShiftPressTime < 300) {
          shiftLock = !shiftLock;
          lastShiftPressTime = 0; // reset
          if (shiftLock) {
            activateShortcutMode();
          } else {
            deactivateShortcutMode();
          }
        } else {
          lastShiftPressTime = now;
          if (!shiftLock) {
            isShiftHeld = true;
            activateShortcutMode();
          }
        }
        updateShortcutOverlay();
      }
      return;
    }

    if (e.key === 'Alt') {
      isAltHeld = true;
      updateShortcutOverlay();
      return;
    }

    // Capture keystrokes in shortcut overlay mode
    if (shortcutModeActive) {
      handleKeystrokeInShortcutMode(e);
      return;
    }

    const dotNetHelper = boardHelper || layoutHelper;
    if (!dotNetHelper) return;

    // 1. Global shortcut: Ctrl+Z / Cmd+Z (Undo)
    const isUndo = (e.ctrlKey || e.metaKey) && e.code === 'KeyZ';

    if (isEdit) {
      // Prevent browser from scrolling when Arrow keys are pressed on select slots or popover list items
      if (activeElement && (activeElement.classList.contains('mud-input-slot') || activeElement.closest('.mud-popover') || activeElement.closest('.mud-list')) && ['ArrowUp', 'ArrowDown'].includes(e.key)) {
        e.preventDefault();
      }
      return;
    }

    // Keyboard scrolling when not editing
    const scrollKeys = ['ArrowDown', 'ArrowUp', 'Space', ' ', 'PageDown', 'PageUp', 'Home', 'End', 'j', 'J', 'k', 'K'];
    if (scrollKeys.includes(e.key)) {
      if (e.ctrlKey || e.metaKey || e.altKey) {
        return;
      }
      // Check if interactive element should consume the key
      if ((e.key === ' ' || e.key === 'Space') && isInteractiveElement(activeElement)) {
        return;
      }
      if (['ArrowDown', 'ArrowUp'].includes(e.key) && needsArrowKeys(activeElement)) {
        return;
      }

      e.preventDefault();

      const activeContainer = getActiveOpenContainer();
      const target = activeContainer ? getScrollableContainer(activeContainer) : window;

      const scrollSpeed = 100; // px
      const pageSpeed = target === window ? window.innerHeight * 0.8 : target.clientHeight * 0.8;

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
      return;
    }

    if (isUndo) {
      e.preventDefault();
      dotNetHelper.invokeMethodAsync("OnCtrlZPressed").catch(function () {});
      return;
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
      if (!shiftLock) {
        deactivateShortcutMode();
      }
    }
  }

  function onWindowBlur() {
    isAltHeld = false;
    isShiftHeld = false;
    shiftLock = false;
    deactivateShortcutMode();
    updateShortcutOverlay();
  }

  function ensureListeners() {
    if (isListenersAdded) return;
    window.addEventListener("keydown", onKeyDown, true); // Use capture phase to intercept input keys in shortcut mode
    window.addEventListener("keyup", onKeyUp);
    window.addEventListener("blur", onWindowBlur);
    isListenersAdded = true;
  }

  return {
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
    },
    // Compatibility methods for old calls
    start: function (dotNetRef) {
      boardHelper = dotNetRef;
      ensureListeners();
    },
    stop: function () {
      boardHelper = null;
      deactivateShortcutMode();
    },
    toggle: function () {
      if (shortcutModeActive) {
        shiftLock = false;
        deactivateShortcutMode();
      } else {
        shiftLock = true;
        activateShortcutMode();
      }
      updateShortcutOverlay();
    }
  };
})();
