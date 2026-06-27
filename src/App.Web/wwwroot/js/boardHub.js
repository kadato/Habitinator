// Requires global `signalR` from @microsoft/signalr (see App.razor).
globalThis.HabitinatorBoardHub = (function () {
  let connection = null;
  let dotNetHelper = null;

  return {
    start: function (helper) {
      const signalR = globalThis.signalR;
      if (!signalR) {
        return Promise.reject(new Error("signalR is not defined"));
      }
      dotNetHelper = helper;
      const url = new URL("hubs/board", document.baseURI).href;
      connection = new signalR.HubConnectionBuilder()
        .withUrl(url, { withCredentials: true })
        .withAutomaticReconnect()
        .build();
      connection.on("BoardChanged", function () {
        const h = dotNetHelper;
        if (!h) {
          return;
        }
        h.invokeMethodAsync("OnBoardChanged").catch(function (err) {
          console.warn("Habitinator board hub: UI refresh failed", err);
        });
      });
      return connection.start();
    },
    stop: function () {
      dotNetHelper = null;
      const c = connection;
      connection = null;
      return c ? c.stop() : Promise.resolve();
    }
  };
})();
