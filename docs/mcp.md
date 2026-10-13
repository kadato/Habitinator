# MCP reference

The MCP endpoint is `POST /mcp`. It uses Streamable HTTP. The endpoint needs the `BoardOrJwt` policy and sits behind the `api` rate limit window.

## Authentication

The endpoint accepts a JWT bearer token from `/api/auth/login` and the auth cookie. Without a token, the server returns 401. When the user record is missing, the tool throws `InvalidRequest` with "Sign in required".

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

Mutations need item ids. `get_snapshot` and `search_items` return the ids. `habit_plus` and `habit_minus` move habits. `daily_complete_for_date` completes past dailies. `toggle_item` flips dailies and todos for today. Past daily completion is append-only, and no tool reverses a past daily completion. `delete_item` runs only with `confirmToken` set to the item id. `import_data` runs only with `confirmToken` set to `CONFIRM`.

## Formats

Dates look like `yyyy-MM-dd`. Times look like `HH:mm` and stay within one day. The server rejects bad values and out-of-range counters, intervals, and weekdays with `InvalidParams`. It never clamps them. A retry with fixed values succeeds. `log_activity` and `log_timer_session` accept `eventId`. Reuse the same `eventId` on retry. The server then logs the event once.

## Tools

The board tools are `get_snapshot`, `get_item`, `get_archived`, `get_sync_delta`, `get_streaks`, `list_tags`, `search_items`, `reorder_item`, `create_item`, `rename_item`, `toggle_item`, `archive_item`, `unarchive_item`, and `delete_item`.

`create_item` takes `section` and `title`. It also takes optional `notes`, `tags`, and `date`. For Daily, the date sets the start date. For Todo, the date sets the due date. Omit the date for Habit.

`search_items` filters by `query`, `tag`, `section`, and `limit`. The limit runs 1 to 200 and defaults to 50. It finds one item without a full snapshot read.

The checklist tools are `get_checklist`, `add_checklist_item`, `set_checklist_item_done`, `remove_checklist_item`, and `set_checklist`. The daily tools are `daily_complete_for_date`, `daily_skip_for_date`, and `update_daily`. The habit tools are `habit_plus`, `habit_minus`, and `update_habit`. The todo tool is `update_todo`.

The activity tools are `get_overview`, `get_dashboard`, `get_daily_contributions`, `get_habit_contributions`, `get_day_detail`, and `log_activity`. The timer tool is `log_timer_session`. The data tools are `export_data` and `import_data`. Import JSON must stay under 10 MB. The settings tools are `get_preferences`, `put_preferences`, `set_preferences_json`, `get_notification_settings`, `put_notification_settings`, and `set_notification_settings_json`. The server rejects bad `HH:mm` values and out-of-range pomodoro numbers with `InvalidParams`. It never ignores them.

Read-only tools carry the `ReadOnly` flag. `delete_item` and `import_data` carry `Destructive`. Repeatable updates carry `Idempotent`. That group is `update_*`, `put_*`, `set_*`, `reorder_item`, `set_checklist_item_done`, `set_checklist`, `log_activity`, and `log_timer_session`. The `log_*` tools count as repeatable only with a reused `eventId`.

## Prompts

The prompts are `plan_day`, `review_week`, and `habit_coach`. `plan_day` takes optional `date` and `tag`. A bad date fails with `InvalidParams`. `review_week` takes `period` and `tag`. `habit_coach` takes `habitId`. Each prompt returns one user message with JSON detail. When the board holds more than 30 dailies or todos, the `plan_day` text says that the preview truncates the board.

## Resources

The resources are `board://snapshot`, `board://archived`, `board://item/{id}`, `activity://dashboard`, and `activity://day/{date}`. The snapshot holds the first 200 rows. The server rejects bad ids and dates with `InvalidParams`.

Tests in `tests/App.Web.IntegrationTests/McpEndpointTests.cs` assert the tool, prompt, and resource names.
