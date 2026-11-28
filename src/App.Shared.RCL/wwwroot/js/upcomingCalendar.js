// Moves DOM focus to one month-grid day. Blazor owns the roving tabindex
// state and calls here after arrow keys, including moves into a new month.
globalThis.focusUpcomingDay = function (date) {
    const btn = document.querySelector(`.cal-grid--month [data-cal-date="${date}"]`);
    if (btn) {
        btn.focus();
    }
};
