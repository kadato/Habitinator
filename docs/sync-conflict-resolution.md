# Sync conflict resolution strategy

Habitinator uses a local-first architecture with an outbound outbox queue. MAUI persists the local mirror in SQLite. The web app persists it in IndexedDB with an in-memory read cache. Both hosts run the same data service, `LocalFirstBoardDataService`, over the `IBoardLocalStore` contract. The system coordinates syncing in the background. If the server returns a version conflict, HTTP 409, the system resolves it without blocking or prompting you.

The conflict resolution algorithm runs in the background and applies two stages of resolution:

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

If all user-facing content fields match exactly, the system treats the conflict as a trivial metadata collision. The system selects the server version. This updates the local database tracking timestamp to align with the server and discards the duplicate outbox operation.

## Last-write-wins, LWW

If content differs, the system applies Last-Write-Wins, LWW, using the most accurate timestamps:

* **Local timestamp.** The time when the edit was enqueued in the local outbox, `BoardOutboxEntry.CreatedAtUtc`. The system converts this value into the server's clock before comparing, using the offset implied by the `Date` header of the 409 response. The conversion removes device clock skew. A device clock that is hours fast or slow no longer decides the winner. If the header is missing, the raw local time is used.
* **Server timestamp.** The time when the item was last updated on the server, `BoardItem.ServerUpdatedAtUtc`.

The conversion is accurate to one network round trip, a few seconds. Two edits within that window still resolve by the raw comparison.

### Resolution paths

* **Local edit is newer, `LocalTime >= ServerTime`.** The system keeps the local device version. The system updates the outbox entry with the server's newer concurrency version and the expected version header and retries it. The server accepts it on the next attempt.
* **Server edit is newer, `LocalTime < ServerTime`.** The system keeps the server version. The system deletes the conflicting local outbox operation and updates the local mirror with the server's newer properties.

## Why SQLite and IndexedDB can mirror PostgreSQL

Sync goes through the HTTP API. The client and the server share three data types: `BoardSnapshot`, `BoardSyncDelta`, and `BoardItem`. There is no shared schema and no log shipping between the databases.

`BoardPersistenceService` translates the server storage model into the shared types. PostgreSQL-only details stay on the server:

* `jsonb` columns hold per-user settings, `NotificationSettingsJson` and `UserPreferencesJson`. Those settings sync through separate settings endpoints, never through the board mirror.
* `ChecklistJson` is `text` in PostgreSQL and `TEXT` in SQLite. Every store keeps it as an opaque string. `DailyChecklistJson` validates and normalizes it.
* Native types map to portable equivalents: `uuid` to `TEXT`, `timestamptz` to ISO-8601 `TEXT`, `boolean` to `INTEGER`, `double` to `REAL`, dates to `TEXT`. GIN indexes, cascades, and the idempotency table stay server-side.

The sync protocol enforces correctness: `Idempotency-Key` headers, `X-Board-Expected-Updated-At-Utc` version checks, HTTP 409 with the server row in the body, the outbox queue, and the two stages above. The client mirror is a cache with a write queue. PostgreSQL is the source of truth.
