globalThis.habBoardStore = (function () {
  var DB_NAME = "habitinator-board";
  var STORE_NAME = "kv";
  var STATE_KEY = "board-state-v1";
  var STATE_TS_KEY = "board-state-v1-ts";
  var LS_KEY = "habitinator_board_state_v1";
  var LS_TS_KEY = "habitinator_board_state_v1_ts";
  var dbPromise = null;
  var hiddenHandler = null;

  function openDb() {
    if (dbPromise) {
      return dbPromise;
    }

    dbPromise = new Promise(function (resolve, reject) {
      try {
        var req = indexedDB.open(DB_NAME, 1);
        req.onupgradeneeded = function () {
          req.result.createObjectStore(STORE_NAME);
        };
        req.onsuccess = function () {
          resolve(req.result);
        };
        req.onerror = function () {
          reject(req.error);
        };
      } catch (err) {
        reject(err);
      }
    });

    return dbPromise;
  }

  function idbGet(key) {
    return openDb().then(function (db) {
      return new Promise(function (resolve, reject) {
        try {
          var tx = db.transaction(STORE_NAME, "readonly");
          var store = tx.objectStore(STORE_NAME);
          var q = store.get(key);
          q.onsuccess = function () {
            resolve(q.result == null ? null : q.result);
          };
          q.onerror = function () {
            reject(q.error);
          };
        } catch (err) {
          reject(err);
        }
      });
    });
  }

  function idbSet(key, val) {
    return openDb().then(function (db) {
      return new Promise(function (resolve, reject) {
        try {
          var tx = db.transaction(STORE_NAME, "readwrite");
          var store = tx.objectStore(STORE_NAME);
          var q = store.put(val, key);
          q.onsuccess = function () {
            resolve();
          };
          q.onerror = function () {
            reject(q.error);
          };
        } catch (err) {
          reject(err);
        }
      });
    });
  }

  function lsGet() {
    try {
      return localStorage.getItem(LS_KEY);
    } catch (err) {
      return null;
    }
  }

  function toTs(val) {
    var n = parseInt(val, 10);
    return isNaN(n) ? 0 : n;
  }

  function lsGetTs() {
    try {
      return toTs(localStorage.getItem(LS_TS_KEY));
    } catch (err) {
      return 0;
    }
  }

  function lsSet(raw) {
    try {
      localStorage.setItem(LS_KEY, raw);
    } catch (err) {
    }
  }

  function lsSetTs(ts) {
    try {
      localStorage.setItem(LS_TS_KEY, String(ts));
    } catch (err) {
    }
  }

  return {
    isOnline: function () {
      return navigator.onLine !== false;
    },
    load: function () {
      // IndexedDB writes are async, localStorage writes are sync. After a crash
      // the two copies can disagree, so load both timestamps and keep the newest.
      var idbRaw = idbGet(STATE_KEY).catch(function () {
        return null;
      });
      var idbTs = idbGet(STATE_TS_KEY).catch(function () {
        return 0;
      });
      return Promise.all([idbRaw, idbTs]).then(function (vals) {
        var raw = vals[0];
        var ts = toTs(vals[1]);
        var lsRaw = lsGet();
        var lsTs = lsGetTs();
        if (lsRaw != null && (raw == null || lsTs > ts)) {
          return lsRaw;
        }

        if (raw != null) {
          return raw;
        }

        return lsRaw;
      }).catch(function () {
        return lsGet();
      });
    },
    save: function (raw) {
      var ts = Date.now();
      lsSet(raw);
      lsSetTs(ts);
      return idbSet(STATE_KEY, raw).then(function () {
        return idbSet(STATE_TS_KEY, ts);
      }).catch(function () {});
    },
    onHidden: function (dotNetRef) {
      if (hiddenHandler) {
        return;
      }

      hiddenHandler = function () {
        if (document.visibilityState === "hidden") {
          try {
            dotNetRef.invokeMethodAsync("OnHidden");
          } catch (err) {
          }
        }
      };
      document.addEventListener("visibilitychange", hiddenHandler);
    },
    clearHidden: function () {
      if (hiddenHandler) {
        document.removeEventListener("visibilitychange", hiddenHandler);
        hiddenHandler = null;
      }
    }
  };
})();
