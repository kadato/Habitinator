// Command palette focus management and smooth scrolling
globalThis.HabitinatorCommandPalette = (function () {
  let previousActiveElement = null;

  return {
    onOpen: function () {
      previousActiveElement = document.activeElement;
      requestAnimationFrame(function () {
        const input = document.querySelector('.cmd-palette-input');
        if (input) {
          input.focus();
          input.select();
        }
      });
    },

    onClose: function () {
      if (previousActiveElement && typeof previousActiveElement.focus === 'function' && document.body.contains(previousActiveElement)) {
        try {
          previousActiveElement.focus();
        } catch (e) {
          // ignore focus failures on removed elements
        }
      }
      previousActiveElement = null;
    },

    scrollSelectedIntoView: function (itemId) {
      requestAnimationFrame(function () {
        const selector = itemId ? `#cmd-item-${itemId}` : '.cmd-palette-item--selected';
        const selected = document.querySelector(selector);
        const container = document.getElementById('cmd-palette-results');
        if (!selected || !container) return;

        const allItems = container.querySelectorAll('.cmd-palette-item');
        if (allItems.length > 0 && selected === allItems[0]) {
          container.scrollTop = 0;
          return;
        }

        selected.scrollIntoView({ block: 'nearest' });
      });
    }
  };
})();
