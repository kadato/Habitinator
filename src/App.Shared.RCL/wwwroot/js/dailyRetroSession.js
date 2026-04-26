// UTC calendar day (yyyy-MM-dd) for which the user last dismissed the "yesterday dailies" prompt.
window.habitinatorGetDailyRetroResolved = function () {
    try {
        return localStorage.getItem("habitinator.dailyRetro.ymd") || "";
    } catch (e) {
        return "";
    }
};

window.habitinatorSetDailyRetroResolved = function (ymd) {
    try {
        localStorage.setItem("habitinator.dailyRetro.ymd", ymd);
    } catch (e) {
    }
};
