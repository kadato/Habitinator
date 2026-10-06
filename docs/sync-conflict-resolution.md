# Sync conflict resolution strategy

Habitinator is local-first with an outbound outbox queue. MAUI keeps the local mirror in SQLite. The web app keeps it in IndexedDB with an in-memory read cache. Both hosts run the same data service, `LocalFirstBoardDataService`, over the `IBoardLocalStore` contract. Sync runs in the background. On a version conflict, HTTP 409, the client resolves it without blocking you and without prompting you.

Resolution runs in two stages:

## Content-aware verification

Before comparing timestamps, the system checks if the local version's contents match the server's version exactly. The system compares the following fields:

* Title
* Completion status, `IsCompleted`
* Habit counter, `Counter` and `NegativeCounter`
* Notes and Tags
* Checklist items, `ChecklistJson`
* Section-specific configurations, for example reset periods, due dates, and start dates
* Drag-and-drop ordering, `SortOrder`
* Archive status, `IsArchived`

If all user-facing content fields match exactly, the conflict is a trivial metadata collision. The client keeps the server version. It moves the local tracking timestamp to the server value and drops the duplicate outbox operation.

## Last-Write-Wins (LWW)

If content differs, the client applies Last-Write-Wins using two timestamps:

* **Local timestamp.** The time the edit entered the local outbox, `BoardOutboxEntry.CreatedAtUtc`. The client converts this value into the server clock before comparing. The offset comes from the `Date` header of the 409 response. The conversion removes device clock skew. A device clock that runs hours fast or slow no longer decides the winner. If the header is missing, the client compares the raw local time.
* **Server timestamp.** The time when the item was last updated on the server, `BoardItem.ServerUpdatedAtUtc`.

The conversion is accurate to one network round trip. Two edits within that window still resolve by the raw comparison.

### Resolution paths

* **Local edit is newer, `LocalTime >= ServerTime`.** The client keeps the local device version. It rewrites the outbox entry with the server concurrency version and retries it. The server accepts the retry.
* **Server edit is newer, `LocalTime < ServerTime`.** The client keeps the server version. It deletes the conflicting local outbox operation and writes the server properties into the local mirror.

## Why SQLite and IndexedDB can mirror PostgreSQL

Sync goes through the HTTP API. The client and the server share three data types: `BoardSnapshot`, `BoardSyncDelta`, and `BoardItem`. There is no shared schema and no log shipping between the databases.

`BoardPersistenceService` translates the server storage model into the shared types. PostgreSQL-only details stay on the server:

* `jsonb` columns hold per-user settings, `NotificationSettingsJson` and `UserPreferencesJson`. Those settings sync through separate settings endpoints, never through the board mirror.
* `ChecklistJson` is `text` in PostgreSQL and `TEXT` in SQLite. Every store keeps it as an opaque string. `DailyChecklistJson` validates and normalizes it.
* Native types map to portable equivalents: `uuid` to `TEXT`, `timestamptz` to ISO-8601 `TEXT`, `boolean` to `INTEGER`, `double` to `REAL`, dates to `TEXT`. GIN indexes, cascades, and the idempotency table stay server-side.

The sync protocol enforces correctness: `Idempotency-Key` headers, `X-Board-Expected-Updated-At-Utc` version checks, HTTP 409 with the server row in the body, the outbox queue, and the two stages above. The client mirror is a cache with a write queue. PostgreSQL is the source of truth.

## Protocol versioning

`BoardSnapshot.ProtocolVersion`, `BoardSyncDelta.ProtocolVersion`, and `UserDataExportDto.FormatVersion` all carry `BoardProtocolVersion.Current`. Every board read and mutation response also sends it in `X-Board-Protocol-Version`. Read the current value in `src/App.Shared.RCL/Models/BoardProtocolVersion.cs`.

If the delta version is newer than the client understands, the client discards the delta and loads a full snapshot. Snapshots cached before versioning deserialize to 0, and the client reads them as version 1. The import service reads export files without a version as version 1.
