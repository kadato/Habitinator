// When the tab or WebView becomes visible again, reload the board (covers missed SignalR pushes).
window.HabitinatorBoardVisibility = (function () {
  let dotNetHelper = null;

  function onVisibilityChange() {
    if (!dotNetHelper || document.visibilityState !== "visible") {
      return;
    }
    dotNetHelper.invokeMethodAsync("OnBecameVisible").catch(function () {});
  }

  return {
    start: function (dotNetRef) {
      dotNetHelper = dotNetRef;
      document.addEventListener("visibilitychange", onVisibilityChange);
    },
    stop: function () {
      document.removeEventListener("visibilitychange", onVisibilityChange);
      dotNetHelper = null;
    }
  };
})();
