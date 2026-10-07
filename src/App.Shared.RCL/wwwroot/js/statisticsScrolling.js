// Snaps heatmap and week bar wrappers to the end, the rightmost edge, on load and filter changes.
// Snaps instantly. The wraps use CSS smooth scrolling for user gestures. That smoothing would turn
// a programmatic scrollLeft assignment into a slow crawl across the whole grid.
globalThis.scrollHeatmapsToEnd = function () {
    requestAnimationFrame(() => {
        const wraps = document.querySelectorAll('.stats-heatmap-wrap');
        wraps.forEach(wrap => {
            const max = wrap.scrollWidth - wrap.clientWidth;
            if (wrap.scrollLeft < max - 2) {
                const prev = wrap.style.scrollBehavior;
                wrap.style.scrollBehavior = 'auto';
                wrap.scrollLeft = wrap.scrollWidth;
                wrap.style.scrollBehavior = prev;
            }
        });
        const weekBars = document.querySelectorAll('.stats-week-bars');
        weekBars.forEach(bar => {
            const max = bar.scrollWidth - bar.clientWidth;
            if (bar.scrollLeft < max - 2) {
                const prev = bar.style.scrollBehavior;
                bar.style.scrollBehavior = 'auto';
                bar.scrollLeft = bar.scrollWidth;
                bar.style.scrollBehavior = prev;
            }
        });
    });
};

// Moves DOM focus to one heatmap cell. Blazor owns the roving tabindex state
// and calls here after arrow keys. Scoped by grid id, since a page can hold
// several grids at once.
globalThis.focusHeatmapCell = function (gridId, row, col) {
    const btn = document.querySelector(
        `.stats-heatmap-grid[data-grid="${gridId}"] .stats-heatmap-day-btn[data-row="${row}"][data-col="${col}"]`);
    if (btn) {
        btn.focus();
    }
};
