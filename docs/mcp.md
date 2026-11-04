# MCP reference

The server card lives at `/.well-known/mcp/server-card.json`. The capability catalog lives at `/.well-known/ai-catalog.json`. The agent skill lives at `/.well-known/agent-skills/habitinator/SKILL.md`. The auth guide lives at `/auth.md`. `Link` headers, `/.well-known/api-catalog`, and `llms.txt` advertise all four.

The MCP endpoint is `POST /mcp`. It uses Streamable HTTP in stateless mode. Reads allow 1000 requests per minute in development and test environments, and 600 per minute in production. `delete_item`, `delete_tag`, and `import_data` allow 100 per minute in development and test environments, and 60 per minute in production.

## Authentication

The MCP endpoint supports two authentication methods:
1. **OAuth 2.1 (Recommended):** Modern MCP clients negotiate access via standard discovery (RFC 9728 at `/.well-known/oauth-protected-resource` and RFC 8414 at `/.well-known/oauth-authorization-server`) and the authorization code flow with PKCE (RFC 7636). When you connect, the client opens your browser to approve access. No secrets go in config files.
2. **Personal Bearer Tokens:** Long-lived tokens minted in the Settings panel or `/api/account/mcp-token` for scripts, headless environments, and terminal use.

Without authentication, the server returns 401 with a `WWW-Authenticate` header pointing to the RFC 9728 protected resource metadata so clients can automatically initiate OAuth.

### Set up a zero-key OAuth 2.1 client

Claude Desktop, Cursor, and VS Code:

```json
{
  "mcpServers": {
    "habitinator": {
      "url": "https://habitinator.app/mcp"
    }
  }
}
```

OpenCode `opencode.jsonc`:

```jsonc
{
  "mcp": {
    "servers": {
      "habitinator": {
        "type": "remote",
        "url": "https://habitinator.app/mcp",
        "oauth": true
      }
    }
  }
}
```

Claude Code CLI:

```bash
claude mcp add habitinator https://habitinator.app/mcp
```

OpenCode CLI:

```bash
opencode mcp add habitinator --url https://habitinator.app/mcp
```

### Connect with personal tokens

Tokens are minted in the Settings panel under Settings, AI assistant, External AI access (MCP). Tokens support three scopes. `full` gives complete access. `tracking_only` gives read tools, habit and daily completion, and timer logging. `read_only` gives read tools, prompts, and resources. The panel tests each token and copies the client config with the token filled in. The panel lists and revokes tokens. Revocation takes effect immediately.

### Client configs

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

The Settings panel shows the exact snippet with your token filled in.

### Mint a token from the terminal

If you prefer the terminal over the Settings panel, call `POST /api/account/mcp-token` with a login JWT. The call mints a dedicated long-lived token valid from 1 to 365 days, with an optional `scope` of `full`, `tracking_only`, or `read_only`:

```bash
curl -s -X POST $BASE/api/account/mcp-token \
	-H "Authorization: Bearer $TOKEN" \
	-H 'Content-Type: application/json' \
	-d '{"days":90,"label":"Claude Desktop","scope":"full"}'
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

### Approve headless access without pasted secrets

Terminal agents that cannot open a browser use the OAuth device authorization grant (RFC 8628). The agent calls `POST /oauth/device/authorize` with its `client_id` and shows you the `verification_uri` and the short `user_code`. You approve once in your browser. The agent polls `POST /oauth/token` with `grant_type` `urn:ietf:params:oauth:grant-type:device_code` until it holds an `access_token` and a `refresh_token`. Device codes expire in 10 minutes. Each code works once. The server binds each code to the requesting `client_id`. Refresh tokens rotate on use. The [auth guide](https://habitinator.app/auth.md) documents the full flow.

## Call order

Mutations accept either an `itemId` GUID or a `title` query string. When `itemId` is omitted, the server resolves the item by matching title. The resolver accepts an exact match or a unique prefix, and returns candidate guidance when multiple items match. `get_snapshot` and `search_items` return all IDs. Past daily completion is append-only, and no tool reverses a past daily completion.

`create_item` and `batch_create_items` warn on near-duplicate titles. The check compares items in the same section. It ignores case and accents. A contains match or a trigram score at or above 0.8 triggers the warning. The call fails with `Similar item exists: '<title>' (id: ...)`. Retry with `force: true` to create anyway. `allowDuplicate: true` still works. For reliable retries, pass `idempotencyKey`. Reusing a key with identical fields returns the original item instead of a duplicate. Reposting a batch with the same key creates its items exactly once. Reusing a key with different fields fails and tells you to use a new key. To validate without executing, pass `dryRun: true`.

`delete_item` and `import_data` ask for elicitation confirmation through `InputRequired` with `confirm_delete` and `confirm_import`. To confirm the action without elicitation, pass `confirmToken` set to the item ID or `CONFIRM`. On sessions without MRTR support, the server fails with `InvalidParams` and returns the same retry guidance. The elicitation message names the `confirmToken` retry.

## Formats

Dates accept `yyyy-MM-dd` and relative keywords `today`, `yesterday`, and `tomorrow`. Times look like `HH:mm` and stay within one day. Weekdays can be passed as bitmasks or weekday name arrays, such as `["Mon", "Wed", "Fri"]` or `["Monday", "Wednesday", "Friday"]`. Tags accept a comma-separated string or a native array of strings. `"work,urgent"` and `["work", "urgent"]` store the same canonical value `work,urgent`, up to 500 chars. UI, REST, and MCP share one parser `BoardTagUtil` and one documented format. Checklist fields `checklistJson` and `json` on `set_checklist` accept a JSON string or a native array of `{text, isDone}` objects, up to 8000 chars. The server rejects bad values with `InvalidParams` that names the field, states the expected format, and quotes the received value. It never clamps them. Fix the input and retry. `log_activity` and `log_timer_session` accept an optional `eventId`. To prevent duplicate logs on retry, reuse the same `eventId`.

## Tools

The board tools are `get_snapshot`, `get_board_summary`, `get_item`, `get_archived`, `get_sync_delta`, `get_streaks`, `list_tags`, `search_items`, `reorder_item`, `create_item`, `batch_create_items`, `get_due_digest`, `rename_item`, `toggle_item`, `batch_toggle_items`, `archive_item`, `bulk_archive`, `unarchive_item`, `bulk_unarchive`, `rename_tag`, `delete_tag`, and `delete_item`.

`get_snapshot` reads the board mirror of habits, dailies, and todos. It accepts an optional `limit` and `format`, which defaults to `json` or can be set to `markdown`.

`get_board_summary` returns an overview of active habits, today's completed and pending dailies, active and overdue todos, and streak totals. It accepts an optional `tag` filter and `format`, which defaults to `markdown` or can be set to `json`. Calling `get_board_summary` before the full snapshot saves context tokens.

`create_item` takes `section` and `title`. It also takes optional `notes`, `tags`, `date`, `checklistJson`, `repeat`, `repeatInterval`, `weekdays`, `weekdayNames`, `resetPeriod`, `trackPlus`, `trackMinus`, `counter`, `negativeCounter`, `idempotencyKey`, `dryRun`, `allowDuplicate`, and `force`. For Daily items, the date sets the start date. For Todo items, the date sets the due date. For Habit items, omit the date.

`batch_create_items` creates multiple board items across sections in a single call. It accepts `items` plus an optional batch `idempotencyKey` and `dryRun`. It returns one per-item result per input: `{index, ok, id, item}` for created items and `{index, ok: false, field, error, message}` for failed ones. Valid items persist even when siblings fail, so a retry only needs to fix the failed entries. Reposting the same batch with the same key creates items exactly once. Keep item order stable across retries or pass explicit per-item keys.

`get_due_digest` returns overdue, due-today, and due-tomorrow todos with a `total` count. Each item appears at most once. The tool accepts optional `snoozedIds` to exclude snoozed items. The digest honors notification settings. It stays empty when daily reminders are off, when in-app messages are off, or during quiet hours.

The `section` parameter is optional on all ID-targeted tools and checklist tools. When omitted, the server resolves the section.

`bulk_archive` and `bulk_unarchive` archive and restore multiple items. When archiving more than 5 items, `bulk_archive` asks for confirmation through `InputRequired` or requires `confirmToken` set to `CONFIRM`.

`delete_item` takes `itemId`, optional `section`, and optional `confirmToken`. The first call without `confirmToken` fails with `InvalidParams` or asks for elicitation where supported. It tells you the exact retry: pass `confirmToken` set to the item id string. `bulk_archive` of more than 5 items and `import_data` follow the same pattern with `confirmToken` set to `CONFIRM`. Always retry with the stated token rather than re-asking the user.

`rename_tag` and `delete_tag` update tags across all active items.

`search_items` filters by `query`, `tag`, `section`, `limit`, `includeArchived`, and `format`. `includeArchived` defaults to `false`. `format` defaults to `json` or accepts `markdown`. The limit runs from 1 to 200 and defaults to 50.

`list_tags` returns distinct tags.

`reorder_item` moves an item to a position. A missing item throws `InvalidParams`.

If an item is missing, `get_item`, `rename_item`, `toggle_item`, `archive_item`, `unarchive_item`, `update_daily`, `update_todo`, `update_habit`, `habit_plus`, `habit_minus`, `daily_complete_for_date`, `daily_skip_for_date`, and `get_checklist` throw `InvalidParams`. Version conflicts throw `InvalidRequest`.

The checklist tools are `get_checklist`, `add_checklist_item`, `set_checklist_item_done`, `remove_checklist_item`, and `set_checklist`. The daily tools are `daily_complete_for_date`, `daily_skip_for_date`, and `update_daily`. The habit tools are `habit_plus`, `habit_minus`, and `update_habit`. The todo tool is `update_todo`.

The activity tools are `get_overview`, `get_dashboard`, `get_daily_contributions`, `get_habit_contributions`, `get_day_detail`, `list_activity_events`, and `log_activity`. The timer tools are `log_timer_session` and `get_timer_summary`. The data tools are `export_data` and `import_data`. Import JSON must stay under 10 MB. The settings tools are `get_preferences`, `put_preferences`, `set_preferences_json`, `get_notification_settings`, `put_notification_settings`, and `set_notification_settings_json`. The server rejects bad `HH:mm` values and out-of-range pomodoro numbers with `InvalidParams`.

Read-only tools carry the `ReadOnly` flag: `get_*`, `list_*`, `search_*`. `delete_item`, `delete_tag`, and `import_data` carry `Destructive`. Repeatable updates carry `Idempotent`. That group is `update_*`, `put_*`, `set_*`, `reorder_item`, `set_checklist_item_done`, `set_checklist`, `bulk_archive`, `bulk_unarchive`, `rename_tag`, `log_activity`, and `log_timer_session`. The `log_*` tools count as repeatable only with a reused `eventId`.

## Prompts

The prompts are `plan_day`, `review_week`, `habit_coach`, `daily_standup`, `evening_review`, `backlog_triage`, `break_down_todo`, `streak_recovery`, and `weekly_retro`.
- `plan_day` takes optional `date` and `tag`. A bad date fails with `InvalidParams`. The tag filter applies to dailies and todos with an exact case-insensitive match before the 30-row preview.
- `review_week` takes `period` and `tag`.
- `habit_coach` takes `habitId`. A bad or missing id throws `InvalidParams`.
- `daily_standup` takes optional `date` and `tag`. Summarizes yesterday activity, today pending dailies, active habits, and pending todos to recommend focus areas.
- `evening_review` takes optional `date`. Reviews what was completed today, habit consistency, and unfinished dailies.
- `backlog_triage` takes optional `tag`. Analyzes overdue todos, untagged items, and items ready for archive.
- `break_down_todo` takes `todoId`. Analyzes the todo title, notes, and due date to generate 3 to 7 subtasks formatted for `set_checklist`.
- `streak_recovery` takes `dailyId`. Analyzes the broken streak, repeat schedule, and last completion date to suggest a restart plan.
- `weekly_retro` takes optional `period` and `tag`. Runs a three-part retrospective covering wins, friction, and an experiment for the coming week.

Argument completions suggest valid values for prompts and resource templates. They provide tags for `plan_day`, `review_week`, `daily_standup`, `backlog_triage`, and `weekly_retro`. They suggest periods for `review_week` and `weekly_retro`. They suggest habit IDs for `habit_coach`, todo IDs for `break_down_todo`, and daily IDs for `streak_recovery`. They provide item IDs for `board://item/{id}` and `board://item/{id}.md`. For date parameters, they suggest yesterday, today, and tomorrow. For `activity://day/{date}`, they provide recent date strings.

When the client passes a progress token, `get_snapshot`, `get_sync_delta`, `search_items`, `export_data`, and `import_data` send progress notifications.

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

To test the endpoint with the official `@modelcontextprotocol/inspector`, run `scripts/mcp-inspector.sh`:

```bash
./scripts/mcp-inspector.sh
```

Tests in `tests/App.Web.IntegrationTests/McpEndpointTests.cs` cover the tools, prompts, resources, completions, and token endpoints.

## Machine-checked tool schemas

The markers below are the machine-readable tool parameter schemas. `McpSchemaConformanceTests` parses them and fails the build when a tool signature drifts from this file. `required(...)` lists parameters without defaults. `optional(...)` lists parameters with defaults. Each entry uses `name:type` where `type` is one JSON Schema base type: `string`, `integer`, `number`, `boolean`, `array`. `McpServer`, `RequestContext`, and `CancellationToken` parameters are excluded.

<!-- mcp-tool-schema: get_board_summary required() optional(format:string,tag:string) -->
<!-- mcp-tool-schema: get_snapshot required() optional(limit:integer,format:string) -->
<!-- mcp-tool-schema: get_item required() optional(itemId:string,title:string,section:string) -->
<!-- mcp-tool-schema: get_archived required() optional(limit:integer) -->
<!-- mcp-tool-schema: get_sync_delta required(cursor:string) optional(limit:integer) -->
<!-- mcp-tool-schema: get_streaks required() optional() -->
<!-- mcp-tool-schema: search_items required() optional(query:string,tag:string,section:string,limit:integer,includeArchived:boolean,format:string) -->
<!-- mcp-tool-schema: list_tags required() optional() -->
<!-- mcp-tool-schema: reorder_item required(itemId:string,position:integer) optional(section:string) -->
<!-- mcp-tool-schema: create_item required(section:string,title:string) optional(notes:string,tags:string,date:string,checklistJson:string,repeat:string,repeatInterval:integer,weekdays:integer,weekdayNames:array,resetPeriod:string,trackPlus:boolean,trackMinus:boolean,counter:integer,negativeCounter:integer,idempotencyKey:string,dryRun:boolean,allowDuplicate:boolean,force:boolean) -->
<!-- mcp-tool-schema: batch_create_items required(items:array) optional(idempotencyKey:string,dryRun:boolean) -->
<!-- mcp-tool-schema: get_due_digest required() optional(snoozedIds:array) -->
<!-- mcp-tool-schema: rename_item required(itemId:string,title:string) optional(section:string) -->
<!-- mcp-tool-schema: toggle_item required() optional(itemId:string,title:string,section:string) -->
<!-- mcp-tool-schema: batch_toggle_items required(itemIds:array) optional(section:string) -->
<!-- mcp-tool-schema: archive_item required() optional(itemId:string,title:string,section:string) -->
<!-- mcp-tool-schema: bulk_archive required(itemIds:array) optional(section:string,confirmToken:string) -->
<!-- mcp-tool-schema: unarchive_item required() optional(itemId:string,title:string,section:string) -->
<!-- mcp-tool-schema: bulk_unarchive required(itemIds:array) optional(section:string) -->
<!-- mcp-tool-schema: rename_tag required(oldTag:string,newTag:string) optional() -->
<!-- mcp-tool-schema: delete_tag required(tag:string) optional() -->
<!-- mcp-tool-schema: delete_item required(itemId:string) optional(section:string,confirmToken:string) -->
<!-- mcp-tool-schema: get_checklist required(itemId:string) optional(section:string) -->
<!-- mcp-tool-schema: add_checklist_item required(itemId:string,text:string) optional(section:string) -->
<!-- mcp-tool-schema: set_checklist_item_done required(itemId:string,checklistItemId:string,done:boolean) optional(section:string) -->
<!-- mcp-tool-schema: remove_checklist_item required(itemId:string,checklistItemId:string) optional(section:string) -->
<!-- mcp-tool-schema: set_checklist required(itemId:string,json:string) optional(section:string) -->
<!-- mcp-tool-schema: daily_complete_for_date required(date:string) optional(itemId:string,title:string) -->
<!-- mcp-tool-schema: daily_skip_for_date required(date:string) optional(itemId:string,title:string) -->
<!-- mcp-tool-schema: update_daily required(itemId:string) optional(title:string,notes:string,tags:string,startDate:string,repeat:string,repeatInterval:integer,counter:integer,weekdays:integer,weekdayNames:array,checklistJson:string,sortOrder:number) -->
<!-- mcp-tool-schema: habit_plus required() optional(itemId:string,title:string) -->
<!-- mcp-tool-schema: habit_minus required() optional(itemId:string,title:string) -->
<!-- mcp-tool-schema: update_habit required(itemId:string) optional(title:string,notes:string,tags:string,trackPlus:boolean,trackMinus:boolean,resetPeriod:string,counter:integer,negativeCounter:integer,checklistJson:string,sortOrder:number) -->
<!-- mcp-tool-schema: update_todo required(itemId:string) optional(title:string,notes:string,tags:string,dueDate:string,checklistJson:string,sortOrder:number) -->
<!-- mcp-tool-schema: get_overview required() optional(period:string,tag:string) -->
<!-- mcp-tool-schema: get_dashboard required() optional(period:string,tag:string) -->
<!-- mcp-tool-schema: get_daily_contributions required() optional(period:string,tag:string) -->
<!-- mcp-tool-schema: get_habit_contributions required() optional(period:string,tag:string) -->
<!-- mcp-tool-schema: get_day_detail required(date:string) optional(tag:string) -->
<!-- mcp-tool-schema: list_activity_events required(startDate:string,endDate:string) optional(eventType:string,boardItemId:string,limit:integer) -->
<!-- mcp-tool-schema: log_activity required(eventType:string) optional(boardItemId:string,customLabel:string,eventId:string) -->
<!-- mcp-tool-schema: get_timer_summary required() optional(date:string) -->
<!-- mcp-tool-schema: log_timer_session required(durationSeconds:integer) optional(boardItemId:string,customLabel:string,eventId:string) -->
<!-- mcp-tool-schema: export_data required() optional() -->
<!-- mcp-tool-schema: import_data required(json:string) optional(confirmToken:string) -->
<!-- mcp-tool-schema: get_preferences required() optional() -->
<!-- mcp-tool-schema: put_preferences required() optional(dateFormat:string,dayStartLocalTime:string,timeZoneOverrideId:string,displayName:string,theme:string,pomodoroWorkMinutes:integer,pomodoroShortBreakMinutes:integer,pomodoroLongBreakMinutes:integer,pomodoroCyclesBeforeLongBreak:integer,enableKeyboardShortcuts:boolean) -->
<!-- mcp-tool-schema: set_preferences_json required(json:string) optional() -->
<!-- mcp-tool-schema: get_notification_settings required() optional() -->
<!-- mcp-tool-schema: put_notification_settings required() optional(inAppMessagesEnabled:boolean,showSuccessToasts:boolean,showWarningToasts:boolean,showErrorToasts:boolean,toastDuration:string,dailyReminderEnabled:boolean,dailyReminderTime:string,focusTimerAlertsEnabled:boolean,syncFailureAlertsEnabled:boolean,soundEnabled:boolean,quietHoursEnabled:boolean,quietHoursStartUtc:string,quietHoursEndUtc:string) -->
<!-- mcp-tool-schema: set_notification_settings_json required(json:string) optional() -->
