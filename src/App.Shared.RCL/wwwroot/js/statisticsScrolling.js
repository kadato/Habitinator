// JavaScript helper to scroll heatmap wrapper elements to the end (rightmost edge) on load/filter changes
window.scrollHeatmapsToEnd = function () {
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
window.initializeHeatmapRovingTabindex = function () {
    const grid = document.querySelector('.stats-heatmap-grid');
    if (!grid) return;

    const btns = Array.from(grid.querySelectorAll('.stats-heatmap-day-btn'));
    if (btns.length === 0) return;

    btns.forEach(btn => btn.setAttribute('tabindex', '-1'));

    const todayBtn = grid.querySelector('.stats-heatmap-day--today.stats-heatmap-day-btn');
    if (todayBtn) {
        todayBtn.setAttribute('tabindex', '0');
    } else {
        const lastBtn = btns[btns.length - 1];
        if (lastBtn) {
            lastBtn.setAttribute('tabindex', '0');
        }
    }
};


if (!window.hasHeatmapNavListener) {
    window.hasHeatmapNavListener = true;
    document.addEventListener('keydown', function (e) {
        const active = document.activeElement;
        if (!active || !active.classList.contains('stats-heatmap-day-btn')) {
            return;
        }

        const row = parseInt(active.getAttribute('data-row'), 10);
        const col = parseInt(active.getAttribute('data-col'), 10);
        if (isNaN(row) || isNaN(col)) return;

        const grid = active.closest('.stats-heatmap-grid');
        if (!grid) return;

        let targetBtn = null;

        if (e.key === 'ArrowLeft') {
            let c = col - 1;
            while (c >= 0) {
                targetBtn = grid.querySelector(`.stats-heatmap-day-btn[data-row="${row}"][data-col="${c}"]`);
                if (targetBtn) break;
                c--;
            }
        } else if (e.key === 'ArrowRight') {
            let c = col + 1;
            const colsVar = grid.style.getPropertyValue('--stats-cols');
            const maxC = colsVar ? parseInt(colsVar, 10) : 60;
            while (c < maxC) {
                targetBtn = grid.querySelector(`.stats-heatmap-day-btn[data-row="${row}"][data-col="${c}"]`);
                if (targetBtn) break;
                c++;
            }
        } else if (e.key === 'ArrowUp') {
            let r = row - 1;
            while (r >= 0) {
                targetBtn = grid.querySelector(`.stats-heatmap-day-btn[data-row="${r}"][data-col="${col}"]`);
                if (targetBtn) break;
                r--;
            }
        } else if (e.key === 'ArrowDown') {
            let r = row + 1;
            while (r < 7) {
                targetBtn = grid.querySelector(`.stats-heatmap-day-btn[data-row="${r}"][data-col="${col}"]`);
                if (targetBtn) break;
                r++;
            }
        } else {
            return; // Not an arrow key
        }

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

