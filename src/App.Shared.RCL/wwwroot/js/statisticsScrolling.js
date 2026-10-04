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

// Initializes roving tabindex for every Activity Heatmap on load and reload.
globalThis.initializeHeatmapRovingTabindex = function (root) {
    const scope = root && typeof root.querySelectorAll === 'function' ? root : document;
    const grids = scope.querySelectorAll('.stats-heatmap-grid');

    grids.forEach(grid => {
        const btns = Array.from(grid.querySelectorAll('.stats-heatmap-day-btn'));
        if (btns.length === 0) return;

        btns.forEach(btn => btn.setAttribute('tabindex', '-1'));

        const todayBtn = grid.querySelector('.stats-heatmap-day--today.stats-heatmap-day-btn');
        const firstTabStop = todayBtn ?? btns.at(-1);
        if (firstTabStop) {
            firstTabStop.setAttribute('tabindex', '0');
        }
    });
};


if (!globalThis.hasHeatmapNavListener) {
    globalThis.hasHeatmapNavListener = true;
    document.addEventListener('keydown', function (e) {
        const active = document.activeElement;
        if (!active?.classList.contains('stats-heatmap-day-btn')) {
            return;
        }

        const row = Number.parseInt(active.dataset.row, 10);
        const col = Number.parseInt(active.dataset.col, 10);
        if (Number.isNaN(row) || Number.isNaN(col)) return;

        const grid = active.closest('.stats-heatmap-grid');
        if (!grid) return;

        const targetBtn = findTargetButton(grid, e.key, row, col);

        if (targetBtn) {
            e.preventDefault();
            grid.querySelectorAll('.stats-heatmap-day-btn').forEach(btn => {
                btn.setAttribute('tabindex', '-1');
            });
            targetBtn.setAttribute('tabindex', '0');
            targetBtn.focus();
        }
    });
}

function findTargetButton(grid, key, row, col) {
    switch (key) {
        case 'ArrowLeft': return findLeft(grid, row, col);
        case 'ArrowRight': return findRight(grid, row, col);
        case 'ArrowUp': return findUp(grid, row, col);
        case 'ArrowDown': return findDown(grid, row, col);
        default: return null;
    }
}

function findLeft(grid, row, col) {
    let c = col - 1;
    while (c >= 0) {
        const btn = grid.querySelector(`.stats-heatmap-day-btn[data-row="${row}"][data-col="${c}"]`);
        if (btn) return btn;
        c--;
    }
    return null;
}

function findRight(grid, row, col) {
    let c = col + 1;
    const colsVar = grid.style.getPropertyValue('--stats-cols');
    const maxC = colsVar ? Number.parseInt(colsVar, 10) : 60;
    while (c < maxC) {
        const btn = grid.querySelector(`.stats-heatmap-day-btn[data-row="${row}"][data-col="${c}"]`);
        if (btn) return btn;
        c++;
    }
    return null;
}

function findUp(grid, row, col) {
    let r = row - 1;
    while (r >= 0) {
        const btn = grid.querySelector(`.stats-heatmap-day-btn[data-row="${r}"][data-col="${col}"]`);
        if (btn) return btn;
        r--;
    }
    return null;
}

function findDown(grid, row, col) {
    let r = row + 1;
    while (r < 7) {
        const btn = grid.querySelector(`.stats-heatmap-day-btn[data-row="${r}"][data-col="${col}"]`);
        if (btn) return btn;
        r++;
    }
    return null;
}

