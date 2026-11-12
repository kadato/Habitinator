// Labels MudBlazor dialog containers for assistive technology.
//
// MudBlazor only wires aria-labelledby when the built-in header is rendered.
// Habitinator dialogs use NoHeader with a custom inner header, so the
// role="dialog" container would otherwise have no accessible name
// under axe rule aria-dialog-name. Each dialog marks its inner shell with
// data-dialog-label and this script copies it onto the container.

(function () {
    function focusAndSelectTitle(dialog) {
        var input = dialog.querySelector('.hab-modal__title-input input');
        if (input && !dialog.dataset.titleAutoSelected) {
            dialog.dataset.titleAutoSelected = 'true';
            // EditDialogHeader handles focus with a touch and MAUI guard so the
            // on-screen keyboard stays down on phones. Only act here when that
            // guarded path is unavailable. The HabitinatorDialogFocus API is missing.
            var guard = window.HabitinatorDialogFocus;
            if (guard && typeof guard.shouldAutoFocus === 'function') {
                try {
                    var r = guard.shouldAutoFocus();
                    if (r && typeof r.then === 'function') {
                        r.then(function (ok) { if (ok) { input.focus(); input.select(); } });
                    } else if (r) {
                        setTimeout(function () { input.focus(); input.select(); }, 60);
                    }
                    return;
                } catch (e) { /* Fall through to direct focus. */ }
            }
            setTimeout(function () {
                input.focus();
                input.select();
            }, 60);
        }
    }

    function labelDialogs(root) {
        var dialogs = [];
        if (root instanceof Element) {
            if (root.matches('.mud-dialog[role="dialog"]')) {
                dialogs.push(root);
            }
            dialogs.push(...root.querySelectorAll('.mud-dialog[role="dialog"]'));
        }
        dialogs.forEach(function (dialog) {
            if (!dialog.hasAttribute('aria-label') && !dialog.hasAttribute('aria-labelledby')) {
                var inner = dialog.querySelector('[data-dialog-label]');
                if (inner?.dataset.dialogLabel) {
                    dialog.setAttribute('aria-label', inner.dataset.dialogLabel);
                }
            }
            focusAndSelectTitle(dialog);
        });
    }

    if (document.body) {
        labelDialogs(document);
    }

    var observer = new MutationObserver(function (mutations) {
        mutations.forEach(function (mutation) {
            mutation.addedNodes.forEach(function (node) {
                if (node.nodeType === 1) {
                    labelDialogs(node);
                }
            });
        });
    });

    if (document.body) {
        observer.observe(document.body, { childList: true, subtree: true });
    } else {
        document.addEventListener('DOMContentLoaded', function () {
            labelDialogs(document);
            observer.observe(document.body, { childList: true, subtree: true });
        });
    }
})();
