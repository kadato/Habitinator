// Timezone detection for user local time
window.habitinatorGetUserTimezone = function () {
    try {
        return Intl.DateTimeFormat().resolvedOptions().timeZone || "";
    } catch (e) {
        return "";
    }
};

window.habitinatorGetTimezoneOffsetMinutes = function () {
    try {
        return new Date().getTimezoneOffset();
    } catch (e) {
        return 0;
    }
};
