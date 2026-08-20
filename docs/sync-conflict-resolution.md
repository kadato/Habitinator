# Sync conflict resolution strategy

Habitinator uses a local-first architecture with an outbound SQLite outbox queue. The system coordinates syncing in the background. If the server returns a version conflict, HTTP 409, the system resolves it without blocking or prompting you.

The conflict resolution algorithm runs in the background and applies two stages of resolution:

## 1. Content-aware verification

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

## 2. Last-write-wins, LWW

If content differs, the system applies Last-Write-Wins, LWW, using the most accurate timestamps:

* **Local timestamp.** The time when the edit was enqueued in the local outbox, `BoardOutboxRow.CreatedAtUtc`.
* **Server timestamp.** The time when the item was last updated on the server, `BoardItem.ServerUpdatedAtUtc`.

### Resolution paths

* **Local edit is newer, `LocalTime >= ServerTime`.** The system keeps the local device version. The system updates the outbox entry with the server's newer concurrency version and the expected version header and retries it. The server accepts it on the next attempt.
* **Server edit is newer, `LocalTime < ServerTime`.** The system keeps the server version. The system deletes the conflicting local outbox operation and updates the local SQLite database with the server's newer properties.
