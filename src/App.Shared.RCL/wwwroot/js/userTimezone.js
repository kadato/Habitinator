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
        const updateTheme = () => {
            if (theme === "dark") {
                root.classList.add("dark-theme");
                root.classList.remove("light-theme");
                root.style.colorScheme = "dark";
            } else {
                root.classList.add("light-theme");
                root.classList.remove("dark-theme");
                root.style.colorScheme = "light";
            }
            // Set cookie so the server knows the theme on next load
            document.cookie = "habitinator_theme=" + theme + "; path=/; max-age=31536000; SameSite=Lax";
        };

        if (document.startViewTransition && window.matchMedia('(prefers-reduced-motion: no-preference)').matches) {
            document.startViewTransition(updateTheme);
        } else {
            updateTheme();
        }
    } catch (e) {
        console.error(e);
    }
};

