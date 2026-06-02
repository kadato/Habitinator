window.HabitinatorSortable = {
    instances: {},
    init: function (columnId, containerElement, dotNetRef) {
        if (this.instances[columnId]) {
            this.instances[columnId].destroy();
        }

        if (!containerElement) return;

        this.instances[columnId] = new Sortable(containerElement, {
            animation: 150,
            ghostClass: 'board-sortable-ghost',
            chosenClass: 'board-sortable-chosen',
            dragClass: 'board-sortable-drag',
            handle: '.board-card',
            onEnd: function (evt) {
                var oldIndex = evt.oldIndex;
                var newIndex = evt.newIndex;

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

                dotNetRef.invokeMethodAsync('OnJsReorderAsync', oldIndex, newIndex);
            }
        });
    },
    destroy: function (columnId) {
        if (this.instances[columnId]) {
            this.instances[columnId].destroy();
            delete this.instances[columnId];
        }
    }
};

