globalThis.HabitinatorSortable = {
    instances: {},
    init: function (columnId, containerElement, dotNetRef) {
        if (this.instances[columnId]) {
            if (this.instances[columnId].el === containerElement) {
                this.instances[columnId].dotNetRef = dotNetRef;
                return;
            }
            this.instances[columnId].destroy();
        }

        if (!containerElement) return;

        const isMaui = !!globalThis.habitinatorIsMaui;
        const instance = new Sortable(containerElement, {
            animation: 0,
            ghostClass: 'board-sortable-ghost',
            chosenClass: 'board-sortable-chosen',
            dragClass: 'board-sortable-drag',
            handle: '.board-card',
            forceFallback: isMaui,
            fallbackOnBody: true,
            fallbackClass: 'board-sortable-fallback-drag',
            filter: '.board-sq, .board-check, .board-card__delete, .board-subtask-pill, .board-subtask-cb, [data-no-drag]',
            preventOnFilter: false,
            delay: 150,
            delayOnTouchOnly: true,
            touchStartThreshold: 5,
            fallbackTolerance: 3,
            onEnd: function (evt) {
                const oldIndex = evt.oldIndex;
                const newIndex = evt.newIndex;

                if (oldIndex === undefined || newIndex === undefined || oldIndex === newIndex) {
                    return;
                }

                // Revert the DOM changes made by Sortable immediately
                // so that Blazor can update the state and re-render cleanly.
                if (evt.from && evt.item) {
                    evt.item.remove();
                    if (oldIndex >= evt.from.children.length) {
                        evt.from.appendChild(evt.item);
                    } else {
                        evt.from.insertBefore(evt.item, evt.from.children[oldIndex]);
                    }
                }

                const currentDotNetRef = instance.dotNetRef;
                if (currentDotNetRef) {
                    currentDotNetRef.invokeMethodAsync('OnJsReorderAsync', oldIndex, newIndex);
                }
            }
        });

        instance.dotNetRef = dotNetRef;
        this.instances[columnId] = instance;
    },
    destroy: function (columnId) {
        if (this.instances[columnId]) {
            this.instances[columnId].dotNetRef = null;
            this.instances[columnId].destroy();
            delete this.instances[columnId];
        }
    }
};

