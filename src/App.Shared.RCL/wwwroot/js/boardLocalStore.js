globalThis.habBoardStore = (function () {
  var DB_NAME = "habitinator-board";
  var STORE_NAME = "kv";
  var STATE_KEY = "board-state-v1";
  var STATE_TS_KEY = "board-state-v1-ts";
  var CHUNK_COUNT_KEY = "board-state-v1-chunks";
  var CHUNK_PREFIX = "board-state-v1-chunk-";
  var CHUNK_SIZE = 700000;
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

  function idbDel(key) {
    return openDb().then(function (db) {
      return new Promise(function (resolve) {
        try {
          var tx = db.transaction(STORE_NAME, "readwrite");
          var store = tx.objectStore(STORE_NAME);
          var q = store.delete(key);
          q.onsuccess = function () {
            resolve();
          };
          q.onerror = function () {
            resolve();
          };
        } catch (err) {
          resolve();
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
      try {
        localStorage.removeItem(LS_KEY);
        localStorage.removeItem(LS_TS_KEY);
      } catch (ignored) {}
    }
  }

  function lsSetTs(ts) {
    try {
      localStorage.setItem(LS_TS_KEY, String(ts));
    } catch (err) {
    }
  }

  function splitChunks(raw) {
    var out = [];
    for (var i = 0; i < raw.length; i += CHUNK_SIZE) {
      out.push(raw.slice(i, i + CHUNK_SIZE));
    }
    if (out.length === 0) {
      out.push("");
    }
    return out;
  }

  function loadChunked() {
    return idbGet(CHUNK_COUNT_KEY).then(function (countRaw) {
      var count = toTs(countRaw);
      if (!count || count <= 0 || count > 64) {
        return null;
      }
      var parts = [];
      for (var i = 0; i < count; i++) {
        parts.push(i);
      }
      return Promise.all(parts.map(function (i) {
        return idbGet(CHUNK_PREFIX + i).catch(function () {
          return null;
        });
      })).then(function (chunks) {
        for (var j = 0; j < chunks.length; j++) {
          if (typeof chunks[j] !== "string") {
            return null;
          }
        }
        return chunks.join("");
      });
    }).catch(function () {
      return null;
    });
  }

  function saveChunked(raw) {
    var chunks = splitChunks(raw);
    var writes = [idbSet(CHUNK_COUNT_KEY, String(chunks.length))];
    chunks.forEach(function (part, i) {
      writes.push(idbSet(CHUNK_PREFIX + i, part));
    });
    return Promise.all(writes).then(function () {
      return idbDel(STATE_KEY).catch(function () {});
    }).then(function () {
      return cleanupOldChunks(chunks.length);
    });
  }

  function cleanupOldChunks(keepFrom) {
    var deletions = [];
    for (var i = keepFrom; i < 64; i++) {
      deletions.push(idbDel(CHUNK_PREFIX + i).catch(function () {}));
    }
    return Promise.all(deletions).then(function () {});
  }

  return {
    isOnline: function () {
      return navigator.onLine !== false;
    },
    load: function () {
      var chunkedRaw = loadChunked();
      var chunkedTs = idbGet(CHUNK_COUNT_KEY).catch(function () {
        return 0;
      });
      var idbRaw = idbGet(STATE_KEY).catch(function () {
        return null;
      });
      var idbTs = idbGet(STATE_TS_KEY).catch(function () {
        return 0;
      });
      return Promise.all([chunkedRaw, chunkedTs, idbRaw, idbTs]).then(function (vals) {
        var cRaw = vals[0];
        var lsRaw = lsGet();
        var lsTs = lsGetTs();
        if (cRaw != null && cRaw.length > 0) {
          return cRaw;
        }
        var raw = vals[2];
        var ts = toTs(vals[3]);
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
      if (raw == null) {
        raw = "";
      }
      if (raw.length < 2000000) {
        lsSet(raw);
        lsSetTs(ts);
      } else {
        try {
          localStorage.removeItem(LS_KEY);
          localStorage.removeItem(LS_TS_KEY);
        } catch (err) {}
      }
      return saveChunked(raw).then(function () {
        return idbSet(STATE_TS_KEY, ts);
      }).catch(function () {});
    },
    clear: function () {
      try {
        localStorage.removeItem(LS_KEY);
        localStorage.removeItem(LS_TS_KEY);
      } catch (err) {}
      return idbDel(STATE_KEY).then(function () {
        return idbDel(STATE_TS_KEY);
      }).then(function () {
        return idbDel(CHUNK_COUNT_KEY);
      }).then(function () {
        return cleanupOldChunks(0);
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
      globalThis.addEventListener("pagehide", hiddenHandler);
    },
    clearHidden: function () {
      if (hiddenHandler) {
        document.removeEventListener("visibilitychange", hiddenHandler);
        try {
          globalThis.removeEventListener("pagehide", hiddenHandler);
        } catch (err) {
        }
        hiddenHandler = null;
      }
    }
  };
})();
