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
