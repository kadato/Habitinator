// Labels MudBlazor dialog containers for assistive technology.
//
// MudBlazor only wires aria-labelledby when the built-in header is rendered.
// Habitinator dialogs use NoHeader with a custom inner header, so the
// role="dialog" container would otherwise have no accessible name
// under axe rule aria-dialog-name. Each dialog marks its inner shell with
// data-dialog-label and this script copies it onto the container.

(function () {
    function labelDialogs(root) {
        var dialogs = [];
        if (root instanceof Element) {
            if (root.matches('.mud-dialog[role="dialog"]')) {
                dialogs.push(root);
            }
            dialogs.push(...root.querySelectorAll('.mud-dialog[role="dialog"]'));
        }
        dialogs.forEach(function (dialog) {
            if (dialog.hasAttribute('aria-label') || dialog.hasAttribute('aria-labelledby')) {
                return;
            }
            var inner = dialog.querySelector('[data-dialog-label]');
            if (inner && inner.dataset.dialogLabel) {
                dialog.setAttribute('aria-label', inner.dataset.dialogLabel);
            }
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
