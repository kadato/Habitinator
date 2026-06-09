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

  const availableKeys = ['A', 'C', 'E', 'I', 'J', 'K', 'L', 'M', 'N', 'Q', 'R', 'U', 'V', 'W', 'X', 'Y', 'Z'];

  // Inject Styles dynamically
  function injectStyles() {
    if (document.getElementById('hab-shortcut-styles')) return;
    const style = document.createElement('style');
    style.id = 'hab-shortcut-styles';
    style.textContent = `
      .hab-shortcut-hint {
        position: absolute;
        z-index: 100000;
        display: inline-flex;
        align-items: center;
        justify-content: center;
        padding: 4px 8px;
        font-family: 'Plus Jakarta Sans', 'Outfit', system-ui, -apple-system, sans-serif;
        font-size: 11px;
        font-weight: 700;
        line-height: 1;
        color: #ffffff !important;
        background: rgba(15, 23, 42, 0.85);
        backdrop-filter: blur(6px);
        -webkit-backdrop-filter: blur(6px);
        border: 1px solid rgba(255, 255, 255, 0.15);
        border-radius: 6px;
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.25), inset 0 1px 0 rgba(255, 255, 255, 0.15);
        pointer-events: none;
        transform: translate(-50%, -50%) scale(0);
        transition: transform 0.18s cubic-bezier(0.34, 1.56, 0.64, 1), opacity 0.18s ease;
        text-transform: uppercase;
        letter-spacing: 0.5px;
      }
      .hab-shortcut-hint--visible {
        transform: translate(-50%, -50%) scale(1);
      }
      .hab-shortcut-hint--dimmed {
        opacity: 0.22;
        transform: translate(-50%, -50%) scale(0.85);
      }
      .hab-shortcut-hint--matched {
        background: rgba(16, 185, 129, 0.95) !important;
        border-color: rgba(52, 211, 153, 0.6) !important;
        box-shadow: 0 0 15px rgba(16, 185, 129, 0.6) !important;
        transform: translate(-50%, -50%) scale(1.2) !important;
      }
      .hab-shortcut-hint--input {
        border-color: rgba(56, 189, 248, 0.6);
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.25), 0 0 8px rgba(56, 189, 248, 0.2);
      }
      .hab-shortcut-hint--nav {
        border-color: rgba(139, 92, 246, 0.6);
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.25), 0 0 8px rgba(139, 92, 246, 0.2);
      }
      .hab-shortcut-hint .key-char {
        color: rgba(255, 255, 255, 0.5);
      }
      .hab-shortcut-hint .key-char--typed {
        color: #38bdf8;
        font-weight: 800;
        text-shadow: 0 0 4px rgba(56, 189, 248, 0.6);
      }
      .hab-shortcut-hint .key-char--untyped {
        color: #ffffff;
      }
      .hab-shortcut-hud {
        position: fixed;
        top: 24px;
        left: 50%;
        transform: translateX(-50%) translateY(-100px);
        z-index: 100001;
        display: flex;
        align-items: center;
        gap: 12px;
        padding: 10px 22px;
        font-family: 'Plus Jakarta Sans', 'Outfit', system-ui, -apple-system, sans-serif;
        font-size: 13px;
        font-weight: 600;
        color: #f1f5f9;
        background: rgba(15, 23, 42, 0.85);
        backdrop-filter: blur(12px) saturate(180%);
        -webkit-backdrop-filter: blur(12px) saturate(180%);
        border: 1px solid rgba(255, 255, 255, 0.1);
        border-radius: 9999px;
        box-shadow: 0 10px 30px rgba(0, 0, 0, 0.35);
        pointer-events: none;
        transition: transform 0.25s cubic-bezier(0.34, 1.56, 0.64, 1), opacity 0.25s ease;
        opacity: 0;
      }
      .hab-shortcut-hud--visible {
        transform: translateX(-50%) translateY(0);
        opacity: 1;
      }
      .hab-shortcut-hud-dot {
        width: 8px;
        height: 8px;
        border-radius: 50%;
        background: #34d399;
        box-shadow: 0 0 8px #34d399;
        animation: hab-pulse 1.5s infinite;
      }
      .hab-shortcut-hud-dot--warning {
        background: #f59e0b;
        box-shadow: 0 0 8px #f59e0b;
      }
      .hab-shortcut-hud-badge {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        background: rgba(255, 255, 255, 0.12);
        border: 1px solid rgba(255, 255, 255, 0.18);
        border-radius: 4px;
        padding: 2px 6px;
        font-family: monospace;
        font-size: 11px;
        font-weight: 700;
        color: #38bdf8;
      }
      @keyframes hab-pulse {
        0% { transform: scale(0.95); opacity: 0.5; }
        50% { transform: scale(1.15); opacity: 1; }
        100% { transform: scale(0.95); opacity: 0.5; }
      }
      body.hab-shortcuts-modal-open .hab-shortcuts-legend {
        display: none !important;
      }
    `;
    document.head.appendChild(style);
  }

  function updateShortcutOverlay() {
    if (isEditing()) {
      document.body.classList.remove("hab-show-shortcuts");
      return;
    }

    if (isAltHeld || isShiftHeld) {
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
      activeElement.closest('.mud-input-slot')
    );
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
      if (!el || assignedElements.has(el) || !isElementVisible(el)) return;
      assignedElements.add(el);
      targets.push({ element: el, shortcut, type });
    }

    const activeContainer = getActiveOpenContainer();

    if (activeContainer) {
      const inputs = Array.from(activeContainer.querySelectorAll('input:not([type="hidden"]):not([type="submit"]):not([type="button"]):not([disabled]), textarea:not([disabled]), select:not([disabled]), [contenteditable="true"], .mud-input-slot:not([disabled])'))
        .filter(el => !assignedElements.has(el) && isElementVisible(el));

      const toggles = Array.from(activeContainer.querySelectorAll('.mud-checkbox, .board-subtask-cb, .mud-switch'))
        .filter(el => !assignedElements.has(el) && isElementVisible(el));

      const clickables = Array.from(activeContainer.querySelectorAll('a[href]:not([href="#"]), button:not([disabled]), [role="button"]:not([disabled]), .mud-button-root:not([disabled]), .app-header-profile-btn, .app-header-username-btn, .board-card__title, .board-card__delete, [role="menuitem"]:not([disabled]):not(.mud-list-item-disabled):not([aria-disabled="true"]), .mud-list-item-clickable:not([disabled]):not(.mud-list-item-disabled):not([aria-disabled="true"]), .mud-menu-item:not([disabled]):not(.mud-list-item-disabled):not([aria-disabled="true"])'))
        .filter(el => !assignedElements.has(el) && isElementVisible(el) && !isInsideInputControl(el));

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

    // 3. Dynamic elements allocation (prefix-free)
    const inputs = Array.from(document.querySelectorAll('input:not([type="hidden"]):not([type="submit"]):not([type="button"]):not([disabled]), textarea:not([disabled]), select:not([disabled]), [contenteditable="true"], .mud-input-slot:not([disabled])'))
      .filter(el => !assignedElements.has(el) && isElementVisible(el));

    const toggles = Array.from(document.querySelectorAll('.mud-checkbox, .board-subtask-cb, .mud-switch'))
      .filter(el => !assignedElements.has(el) && isElementVisible(el));

    const clickables = Array.from(document.querySelectorAll('a[href]:not([href="#"]), button:not([disabled]), [role="button"]:not([disabled]), .mud-button-root:not([disabled]), .app-header-profile-btn, .app-header-username-btn, .board-card__title, .board-card__delete, [role="menuitem"]:not([disabled]):not(.mud-list-item-disabled):not([aria-disabled="true"]), .mud-list-item-clickable:not([disabled]):not(.mud-list-item-disabled):not([aria-disabled="true"]), .mud-menu-item:not([disabled]):not(.mud-list-item-disabled):not([aria-disabled="true"])'))
      .filter(el => !assignedElements.has(el) && isElementVisible(el) && !isInsideInputControl(el));

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
    injectStyles();
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
      const top = rect.top + window.scrollY + (rect.height / 2);
      const left = isInputEl 
        ? rect.left + window.scrollX + 16 
        : rect.left + window.scrollX + (rect.width / 2);

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
      if (el.classList.contains('mud-checkbox') || el.classList.contains('board-subtask-cb') || el.classList.contains('mud-switch')) {
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
    // Escape blur functionality (from original codebase)
    const activeElement = document.activeElement;
    const isEdit = isEditing();
    
    if (e.code === 'Escape' || e.key === 'Escape') {
      if (shortcutModeActive) {
        shiftLock = false;
        deactivateShortcutMode();
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
        }, 10);
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
    }
  };
})();
