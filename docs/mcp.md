# MCP reference

The MCP endpoint is `POST /mcp`. It uses Streamable HTTP in stateless mode. The endpoint needs the `BoardOrJwt` policy and sits behind the `mcp` rate limit window. Reads use `{user}:read`, with 1000 requests per minute in development and test environments, and 600 requests per minute sliding window in production. `delete_item` and `import_data` match on the `Mcp-Name` routable header and use `{user}:write`, with 100 requests per minute in development and test environments, and 60 requests per minute in production. The read window stays wider than the 300 requests per minute `api` window so MCP polling does not starve REST.

The server version is synced from the assembly version, not a hardcoded string.

## Authentication

The endpoint accepts a JWT bearer token from `/api/auth/login`, long-lived personal MCP tokens from `/api/account/mcp-token`, and the auth cookie. Without a token, the server returns 401. When the user record is missing, the tool throws `InvalidRequest` with "Sign in required" or "Register or sign in again".

To configure external AI assistants like Claude Desktop or Cursor, call `POST /api/account/mcp-token` to generate a dedicated long-lived token valid from 1 to 365 days:

```bash
curl -s -X POST $BASE/api/account/mcp-token \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"days":90,"label":"Claude Desktop"}'
```

A `tools/list` call looks like this:

```bash
	TOKEN=$(curl -s -X POST $BASE/api/auth/login \
	  -H 'Content-Type: application/json' \
	  -d '{"email":"you@example.com","password":"..."}' | jq -r .accessToken)
	curl -s -X POST $BASE/mcp \
	  -H "Authorization: Bearer $TOKEN" \
	  -H 'Content-Type: application/json' \
	  -H 'Accept: application/json, text/event-stream' \
	  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
```

## Call order

Mutations need item ids. `get_snapshot` and `search_items` return the ids. `habit_plus` and `habit_minus` move habits. `daily_complete_for_date` completes past dailies. `toggle_item` flips dailies and todos for today. `batch_toggle_items` toggles multiple items in one call. Past daily completion is append-only, and no tool reverses a past daily completion. `delete_item` and `import_data` ask for elicitation confirmation through `InputRequired` with `confirm_delete` and `confirm_import`. Passing `confirmToken` set to the item id or `CONFIRM` confirms the action without elicitation. On sessions without MRTR support the server fails with `InvalidParams` and the same retry guidance.

## Formats

Dates use strict `yyyy-MM-dd` via `DateOnly.TryParseExact`. Times look like `HH:mm` and stay within one day. The server rejects bad values and out-of-range counters, intervals, and weekdays with `InvalidParams`. It never clamps them. A retry with fixed values succeeds. `log_activity` and `log_timer_session` accept `eventId`. Reuse the same `eventId` on retry. The server then logs the event once.

## Tools

The board tools are `get_snapshot`, `get_item`, `get_archived`, `get_sync_delta`, `get_streaks`, `list_tags`, `search_items`, `reorder_item`, `create_item`, `rename_item`, `toggle_item`, `batch_toggle_items`, `archive_item`, `bulk_archive`, `unarchive_item`, and `delete_item`.

`create_item` takes `section` and `title`. It also takes optional `notes`, `tags`, `date`, and `checklistJson`. For Daily, the date sets the start date. For Todo, the date sets the due date. Omit the date for Habit. The checklist can be initialized atomically at creation time. The create is atomic in one `CreateItemDetailedAsync` call, no second update.

`search_items` filters by `query`, `tag`, `section`, and `limit`. The limit runs 1 to 200 and defaults to 50. It runs DB-backed with `EF.Functions.Like` on lowercased title, notes, and tags, then applies an exact tag filter in memory with `BoardTagUtil.ParseTags`. It finds one item without a full snapshot read.

`list_tags` runs DB-backed with `GetDistinctTagsAsync`, no snapshot or streak work.

`reorder_item` does a fresh read of the section list, clamps the position, computes the midpoint sort order, and saves. It never uses a cached snapshot, so there is no cached-snapshot race. It throws `InvalidParams` when the item is missing.

`get_item`, `rename_item`, `toggle_item`, `archive_item`, `unarchive_item`, `update_daily`, `update_todo`, `update_habit`, `habit_plus`, `habit_minus`, `daily_complete_for_date`, `daily_skip_for_date`, and `get_checklist` throw `InvalidParams` when the item is missing instead of returning null or an empty list. Version conflicts throw `InvalidRequest`.

The checklist tools are `get_checklist`, `add_checklist_item`, `set_checklist_item_done`, `remove_checklist_item`, and `set_checklist`. The daily tools are `daily_complete_for_date`, `daily_skip_for_date`, and `update_daily`. The habit tools are `habit_plus`, `habit_minus`, and `update_habit`. The todo tool is `update_todo`.

The activity tools are `get_overview`, `get_dashboard`, `get_daily_contributions`, `get_habit_contributions`, `get_day_detail`, and `log_activity`. The timer tool is `log_timer_session`. The data tools are `export_data` and `import_data`. Import JSON must stay under 10 MB. The settings tools are `get_preferences`, `put_preferences`, `set_preferences_json`, `get_notification_settings`, `put_notification_settings`, and `set_notification_settings_json`. The server rejects bad `HH:mm` values and out-of-range pomodoro numbers with `InvalidParams`. It never ignores them.

Read-only tools carry the `ReadOnly` flag. `delete_item` and `import_data` carry `Destructive`. Repeatable updates carry `Idempotent`. That group is `update_*`, `put_*`, `set_*`, `reorder_item`, `set_checklist_item_done`, `set_checklist`, `bulk_archive`, `log_activity`, and `log_timer_session`. The `log_*` tools count as repeatable only with a reused `eventId`.

## Prompts

The prompts are `plan_day`, `review_week`, `habit_coach`, `daily_standup`, `evening_review`, and `backlog_triage`.
- `plan_day` takes optional `date` and `tag`. A bad date fails with `InvalidParams`. The tag filter applies to dailies and todos with an exact case-insensitive match before the 30-row preview.
- `review_week` takes `period` and `tag`.
- `habit_coach` takes `habitId`. A bad id throws `InvalidParams`, and a missing habit throws `InvalidParams`.
- `daily_standup` takes optional `date` and `tag`. Summarizes yesterday activity, today pending dailies, active habits, and pending todos to recommend focus areas.
- `evening_review` takes optional `date`. Reviews what was completed today, habit consistency, and unfinished dailies.
- `backlog_triage` takes optional `tag`. Analyzes overdue todos, untagged items, and items ready for archive.

Completion suggests tag values for the `tag` argument of `plan_day`, `review_week`, `daily_standup`, and `backlog_triage`, period values for `review_week`, habit ids for `habit_coach`, item ids for `board://item/{id}`, and recent date strings for `activity://day/{date}`.

The long tools `get_snapshot`, `get_sync_delta`, `search_items`, `export_data`, and `import_data` send progress notifications when the client passes a progress token. Mutations send best effort `board://snapshot` and `board://item/{id}` resource notifications. On stateless HTTP there is no push channel, so only stateful clients receive them. SignalR already fans out to the UI.

## Resources

The resources are:
- `board://snapshot`: Current active mirror with the first 200 rows by sort order.
- `board://archived`: Archived items hidden from the active board, first 200 rows.
- `board://item/{id}`: One board item by GUID.
- `board://summary`: Summary of active habits, today completed and pending dailies, active and overdue todos, and streaks, in JSON format.
- `board://summary.md`: Summary in Markdown format, structured for low token consumption.
- `board://tags`: Distinct tag taxonomy across active items, in JSON format.
- `activity://dashboard`: Activity heatmap and period statistics.
- `activity://day/{date}`: Day detail events and focus minutes for one date in `yyyy-MM-dd` format.

## Developer tooling

Use `scripts/mcp-inspector.sh` to launch the official `@modelcontextprotocol/inspector` against a local or remote Habitinator instance:

```bash
./scripts/mcp-inspector.sh
```

Tests in `tests/App.Web.IntegrationTests/McpEndpointTests.cs` assert the tools, prompts, resources, completions, and token endpoints.
