# MCP reference

The server card lives at `/.well-known/mcp/server-card.json`. The capability catalog lives at `/.well-known/ai-catalog.json`. The agent skill lives at `/.well-known/agent-skills/habitinator/SKILL.md`. The auth guide lives at `/auth.md`. `Link` headers, `/.well-known/api-catalog`, and `llms.txt` advertise all four.

The MCP endpoint is `POST /mcp`. It uses Streamable HTTP in stateless mode. The endpoint needs the `BoardOrJwt` policy and sits behind the `mcp` rate limit window. Reads use `{user}:read`, with 1000 requests per minute in development and test environments, and 600 requests per minute sliding window in production. `delete_item`, `delete_tag`, and `import_data` match on the `Mcp-Name` routable header and use `{user}:write`. Their rate limit is 100 requests per minute in development and test environments, and 60 requests per minute in production. The read window stays wider than the 300 requests per minute `api` window so MCP polling does not starve REST.

The server version is synced from the assembly version, not a hardcoded string.

## Authentication

The endpoint accepts a JWT bearer token from `/api/auth/login`, long-lived personal MCP tokens from `/api/account/mcp-token`, and the auth cookie. Without a token, the server returns 401. When the user record is missing, the tool throws `InvalidRequest` with "Sign in required" or "Register or sign in again".

### Connect from the app

Tokens are minted in the Settings panel under Settings, AI assistant, External AI access (MCP). The panel tests each token and copies the client config with the token filled in. The panel lists issued tokens with expiry dates and revokes one token or all tokens. Revocation takes effect immediately. auth.md documents the same flow for the terminal.

### Client configs

Claude Desktop, Cursor, and VS Code read `mcpServers` with a `url` plus an `Authorization` header. OpenCode reads `mcp.servers` with `type: remote` and `oauth: false`. Hermes reads `mcp_servers` in `config.yaml`.

OpenCode `opencode.jsonc`:

```jsonc
{
  "mcp": {
    "servers": {
      "habitinator": {
        "type": "remote",
        "url": "https://habitinator.app/mcp",
        "oauth": false,
        "headers": { "Authorization": "Bearer YOUR_MCP_TOKEN" }
      }
    }
  }
}
```

```bash
export HABITINATOR_MCP_TOKEN=YOUR_MCP_TOKEN
opencode mcp add habitinator --url https://habitinator.app/mcp --header 'Authorization=Bearer {env:HABITINATOR_MCP_TOKEN}'
```

Hermes `config.yaml`:

```yaml
mcp_servers:
  habitinator:
    url: "https://habitinator.app/mcp"
    headers:
      Authorization: "Bearer YOUR_MCP_TOKEN"
    timeout: 120
    connect_timeout: 60
    enabled: true
```

Claude Desktop, Cursor, and VS Code use `mcpServers` with `url` plus an `Authorization` header. The Settings panel shows the exact snippet with your token filled in.

### Token minting from the terminal

If you prefer the terminal over the Settings panel, call `POST /api/account/mcp-token` with a login JWT. The call mints a dedicated long-lived token valid from 1 to 365 days:

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

Mutations need item IDs. `get_snapshot` and `search_items` return the IDs. `habit_plus` and `habit_minus` move habits. `daily_complete_for_date` completes past dailies. `toggle_item` flips dailies and todos for today. `batch_toggle_items` toggles multiple items in one call. Past daily completion is append-only, and no tool reverses a past daily completion. `delete_item` and `import_data` ask for elicitation confirmation through `InputRequired` with `confirm_delete` and `confirm_import`. To confirm the action without elicitation, pass `confirmToken` set to the item ID or `CONFIRM`. On sessions without MRTR support, the server fails with `InvalidParams` and returns the same retry guidance.

## Formats

Dates use strict `yyyy-MM-dd` via `DateOnly.TryParseExact`. Times look like `HH:mm` and stay within one day. Weekdays can be passed as bitmasks or weekday name arrays, such as `["Mon", "Wed", "Fri"]` or `["Monday", "Wednesday", "Friday"]`. The server rejects bad values and out-of-range counters, intervals, and weekdays with `InvalidParams`. It never clamps them. A retry with fixed values succeeds. `log_activity` and `log_timer_session` accept an optional `eventId`. To prevent duplicate logs on retry, reuse the same `eventId`.

## Tools

The board tools are `get_snapshot`, `get_board_summary`, `get_item`, `get_archived`, `get_sync_delta`, `get_streaks`, `list_tags`, `search_items`, `reorder_item`, `create_item`, `batch_create_items`, `rename_item`, `toggle_item`, `batch_toggle_items`, `archive_item`, `bulk_archive`, `unarchive_item`, `bulk_unarchive`, `rename_tag`, `delete_tag`, and `delete_item`.

`get_snapshot` reads the board mirror of habits, dailies, and todos. It accepts an optional `limit` and an optional `format`, which defaults to `json` or can be set to `markdown`.

`get_board_summary` returns an overview of active habits, today's completed and pending dailies, active and overdue todos, and streak totals. It accepts an optional `tag` filter and an optional `format`, which defaults to `markdown` or can be set to `json`. Calling `get_board_summary` before the full snapshot saves context tokens.

`create_item` takes `section` and `title`. It also takes optional `notes`, `tags`, `date`, `checklistJson`, `repeat`, `repeatInterval`, `weekdays`, `weekdayNames`, `resetPeriod`, `trackPlus`, `trackMinus`, `counter`, and `negativeCounter`. For Daily items, the date sets the start date. For Todo items, the date sets the due date. For Habit items, omit the date. The checklist can be set at creation time. Creation is atomic through `CreateItemDetailedAsync` without a second update.

`batch_create_items` creates multiple board items across sections in a single call. It accepts `items`, which is an array of objects matching the `create_item` parameters, and returns the array of created items.

The `section` parameter is optional on all ID-targeted tools and checklist tools. When omitted, the server resolves the item's section from the database.

`bulk_archive` and `bulk_unarchive` archive and restore multiple items in a single database update. When archiving more than 5 items, `bulk_archive` asks for confirmation through `InputRequired` or requires `confirmToken` set to `CONFIRM`.

`rename_tag` and `delete_tag` update tags across all active items for the user, removing or replacing tags without altering unrelated tags.

`search_items` filters by `query`, `tag`, `section`, `limit`, `includeArchived`, and `format`. `includeArchived` defaults to `false`. `format` defaults to `json` or accepts `markdown`. The limit runs from 1 to 200 and defaults to 50. In SQL, it filters with pattern matching before taking rows. In memory, it validates exact tag boundaries with `BoardTagUtil.ParseTags`.

`list_tags` queries distinct tags directly with `GetDistinctTagsAsync` without reading the board snapshot or streaks.

`reorder_item` reads the current section list, clamps the target position, computes the midpoint sort order, and saves. It reads directly from the database without a cached snapshot. If the item is missing, it throws `InvalidParams`.

If an item is missing, `get_item`, `rename_item`, `toggle_item`, `archive_item`, `unarchive_item`, `update_daily`, `update_todo`, `update_habit`, `habit_plus`, `habit_minus`, `daily_complete_for_date`, `daily_skip_for_date`, and `get_checklist` throw `InvalidParams`. Version conflicts throw `InvalidRequest`.

The checklist tools are `get_checklist`, `add_checklist_item`, `set_checklist_item_done`, `remove_checklist_item`, and `set_checklist`. The daily tools are `daily_complete_for_date`, `daily_skip_for_date`, and `update_daily`. The habit tools are `habit_plus`, `habit_minus`, and `update_habit`. The todo tool is `update_todo`.

The activity tools are `get_overview`, `get_dashboard`, `get_daily_contributions`, `get_habit_contributions`, `get_day_detail`, `list_activity_events`, and `log_activity`. The timer tools are `log_timer_session` and `get_timer_summary`. The data tools are `export_data` and `import_data`. Import JSON must stay under 10 MB. The settings tools are `get_preferences`, `put_preferences`, `set_preferences_json`, `get_notification_settings`, `put_notification_settings`, and `set_notification_settings_json`. The server rejects bad `HH:mm` values and out-of-range pomodoro numbers with `InvalidParams`. It never ignores them.

Read-only tools carry the `ReadOnly` flag: `get_*`, `list_*`, `search_*`. `delete_item`, `delete_tag`, and `import_data` carry `Destructive`. Repeatable updates carry `Idempotent`. That group is `update_*`, `put_*`, `set_*`, `reorder_item`, `set_checklist_item_done`, `set_checklist`, `bulk_archive`, `bulk_unarchive`, `rename_tag`, `log_activity`, and `log_timer_session`. The `log_*` tools count as repeatable only with a reused `eventId`.

## Prompts

The prompts are `plan_day`, `review_week`, `habit_coach`, `daily_standup`, `evening_review`, `backlog_triage`, `break_down_todo`, `streak_recovery`, and `weekly_retro`.
- `plan_day` takes optional `date` and `tag`. A bad date fails with `InvalidParams`. The tag filter applies to dailies and todos with an exact case-insensitive match before the 30-row preview.
- `review_week` takes `period` and `tag`.
- `habit_coach` takes `habitId`. A bad id throws `InvalidParams`, and a missing habit throws `InvalidParams`.
- `daily_standup` takes optional `date` and `tag`. Summarizes yesterday activity, today pending dailies, active habits, and pending todos to recommend focus areas.
- `evening_review` takes optional `date`. Reviews what was completed today, habit consistency, and unfinished dailies.
- `backlog_triage` takes optional `tag`. Analyzes overdue todos, untagged items, and items ready for archive.
- `break_down_todo` takes `todoId`. Analyzes the todo title, notes, and due date to generate 3 to 7 subtasks formatted for `set_checklist`.
- `streak_recovery` takes `dailyId`. Analyzes the broken streak, repeat schedule, and last completion date to suggest a restart plan.
- `weekly_retro` takes optional `period` and `tag`. Runs a three-part retrospective covering wins, friction, and an experiment for the coming week.

Argument completions suggest valid values for prompts and resource templates. They provide tags for `plan_day`, `review_week`, `daily_standup`, `backlog_triage`, and `weekly_retro`. They suggest periods for `review_week` and `weekly_retro`. They suggest habit IDs for `habit_coach`, todo IDs for `break_down_todo`, and daily IDs for `streak_recovery`. They provide item IDs for `board://item/{id}` and `board://item/{id}.md`. For date parameters, they suggest yesterday, today, and tomorrow. For `activity://day/{date}`, they provide recent date strings.

When the client passes a progress token, the long tools `get_snapshot`, `get_sync_delta`, `search_items`, `export_data`, and `import_data` send progress notifications. Mutations send best effort `board://snapshot` and `board://item/{id}` resource notifications. Because stateless HTTP has no push channel, only stateful clients receive them. SignalR fans out updates to the web UI.

## Resources

The resources are:
- `board://snapshot`: Active board mirror in JSON, limited to 200 items.
- `board://archived`: Archived items in JSON, limited to 200 items.
- `board://archived.md`: Archived items in Markdown.
- `board://item/{id}`: Board item by GUID in JSON.
- `board://item/{id}.md`: Board item by GUID in Markdown.
- `board://summary`: Active habits, dailies, todos, and streaks in JSON.
- `board://summary.md`: Board summary in Markdown.
- `board://tags`: Distinct active tags in JSON.
- `board://streaks`: Daily streak counts in JSON.
- `activity://dashboard`: Activity heatmap and period statistics in JSON.
- `activity://dashboard.md`: Activity dashboard in Markdown.
- `activity://day/{date}`: Events and focus minutes for one `yyyy-MM-dd` date in JSON.
- `activity://day/{date}.md`: Day events and focus minutes in Markdown.

## Developer tooling

To test the endpoint with the official `@modelcontextprotocol/inspector` against a local or remote Habitinator instance, run `scripts/mcp-inspector.sh`:

```bash
./scripts/mcp-inspector.sh
```

Tests in `tests/App.Web.IntegrationTests/McpEndpointTests.cs` assert the tools, prompts, resources, completions, and token endpoints.
