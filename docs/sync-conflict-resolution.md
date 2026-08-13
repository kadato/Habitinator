# Sync Conflict Resolution Strategy

Habitinator employs a local-first architecture with an outbound SQLite outbox queue. Syncing is coordinated in the background. If a version conflict (HTTP 409) is returned by the server, the system automatically resolves it without blocking or prompting the user, ensuring a smooth, distraction-free user experience.

The conflict resolution algorithm runs in the background and applies two stages of automatic resolution:

## 1. Content-Aware Verification
Before performing timestamp comparison, the system checks if the local version's contents match the server's version exactly. We compare the following fields:
* Title
* Completion status (`IsCompleted`)
* Habit counter (`Counter` / `NegativeCounter`)
* Notes and Tags
* Checklist items (`ChecklistJson`)
* Section-specific configurations (e.g., reset periods, due dates, start dates)
* Drag-and-drop ordering (`SortOrder`)
* Archive status (`IsArchived`)

If all user-facing content fields match exactly, the conflict is considered a trivial metadata collision. The system automatically selects the **Server Version** (which updates the local database tracking timestamp to align with the server) and discards the duplicate outbox operation silently.

## 2. Last-Write-Wins (LWW)
If there are content differences, the system applies **Last-Write-Wins (LWW)** using the most accurate timestamps:
* **Local Timestamp**: The time when the edit was enqueued in the local outbox (`BoardOutboxRow.CreatedAtUtc`).
* **Server Timestamp**: The time when the item was last updated on the server (`BoardItem.ServerUpdatedAtUtc`).

### Resolution Paths:
* **Local edit is newer (`LocalTime >= ServerTime`)**: The local device version is kept. The outbox entry is updated with the server's newer concurrency version (the expected version header) and retried. The server accepts it on the next attempt.
* **Server edit is newer (`LocalTime < ServerTime`)**: The server's version is kept. The conflicting local outbox operation is deleted, and the local SQLite database is updated with the server's newer properties.
