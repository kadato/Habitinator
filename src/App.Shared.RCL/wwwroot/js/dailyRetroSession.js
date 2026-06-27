// UTC calendar day (yyyy-MM-dd) for which the user last dismissed the "yesterday dailies" prompt.
globalThis.habitinatorGetDailyRetroResolved = function () {
    try {
        return localStorage.getItem("habitinator.dailyRetro.ymd") || "";
    } catch (e) {
        console.warn("Failed to get daily retro from localStorage.", e);
        return "";
    }
};

globalThis.habitinatorSetDailyRetroResolved = function (ymd) {
    try {
        localStorage.setItem("habitinator.dailyRetro.ymd", ymd);
    } catch (e) {
        console.error("Failed to set daily retro in localStorage.", e);
    }
};
