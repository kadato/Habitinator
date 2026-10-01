globalThis.habBoardStore = (function () {
  var DB_NAME = "habitinator-board";
  var STORE_NAME = "kv";
  var STATE_KEY = "board-state-v1";
  var LS_KEY = "habitinator_board_state_v1";
  var dbPromise = null;

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

  function idbGet() {
    return openDb().then(function (db) {
      return new Promise(function (resolve, reject) {
        try {
          var tx = db.transaction(STORE_NAME, "readonly");
          var store = tx.objectStore(STORE_NAME);
          var q = store.get(STATE_KEY);
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

  function idbSet(raw) {
    return openDb().then(function (db) {
      return new Promise(function (resolve, reject) {
        try {
          var tx = db.transaction(STORE_NAME, "readwrite");
          var store = tx.objectStore(STORE_NAME);
          var q = store.put(raw, STATE_KEY);
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

  function lsSet(raw) {
    try {
      localStorage.setItem(LS_KEY, raw);
    } catch (err) {
    }
  }

  return {
    isOnline: function () {
      return navigator.onLine !== false;
    },
    load: function () {
      return idbGet().then(function (val) {
        if (val != null) {
          return val;
        }

        return lsGet();
      }).catch(function () {
        return lsGet();
      });
    },
    save: function (raw) {
      lsSet(raw);
      return idbSet(raw).catch(function () {});
    }
  };
})();
