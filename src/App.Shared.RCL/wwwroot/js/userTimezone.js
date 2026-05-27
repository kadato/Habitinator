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

window.habitinatorSetTheme = function (theme) {
    try {
        const root = document.documentElement;
        if (theme === "dark") {
            root.classList.add("dark-theme");
            root.classList.remove("light-theme");
            root.style.colorScheme = "dark";
        } else {
            root.classList.add("light-theme");
            root.classList.remove("dark-theme");
            root.style.colorScheme = "light";
        }
    } catch (e) {
        console.error(e);
    }
};

