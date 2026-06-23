// JavaScript helper to scroll heatmap wrapper elements to the end (rightmost edge) on load/filter changes
globalThis.scrollHeatmapsToEnd = function () {
    requestAnimationFrame(() => {
        setTimeout(() => {
            const wraps = document.querySelectorAll('.stats-heatmap-wrap');
            wraps.forEach(wrap => {
                wrap.scrollLeft = wrap.scrollWidth;
            });
        }, 50);
    });
};

// JavaScript helper to initialize roving tabindex for the Activity Heatmap on load/reload
globalThis.initializeHeatmapRovingTabindex = function () {
    const grid = document.querySelector('.stats-heatmap-grid');
    if (!grid) return;

    const btns = Array.from(grid.querySelectorAll('.stats-heatmap-day-btn'));
    if (btns.length === 0) return;

    btns.forEach(btn => btn.setAttribute('tabindex', '-1'));

    const todayBtn = grid.querySelector('.stats-heatmap-day--today.stats-heatmap-day-btn');
    if (todayBtn) {
        todayBtn.setAttribute('tabindex', '0');
    } else {
        const lastBtn = btns.at(-1);
        if (lastBtn) {
            lastBtn.setAttribute('tabindex', '0');
        }
    }
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

