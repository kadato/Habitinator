---
name: habitinator
description: Use the Habitinator MCP server to read and update a user's habits, dailies, todos, activity, and focus timer. Prefer get_board_summary first, then get_snapshot or search_items for IDs.
---

# Habitinator MCP skill

Habitinator exposes boards over Streamable HTTP MCP at `POST https://habitinator.app/mcp`. Board data is private and needs a Bearer personal MCP token.

## When to use this

Use it when the user asks about their habits, dailies, todos, streaks, activity, or focus sessions in Habitinator.

## Setup

1. Get a token in the app. Open **Settings**, **AI assistant**, **External AI access (MCP)** and select **Generate MCP token**. Tokens support Full, TrackingOnly, and ReadOnly scopes.
2. Without the app, call `POST /api/account/mcp-token` with a login JWT. From a headless terminal, call `POST /oauth/device/authorize` and approve the shown code in your browser. Device codes expire in 10 minutes. Each code works once. The server binds each code to the requesting `client_id`. Refresh tokens rotate on use. The [auth guide](https://habitinator.app/auth.md) documents both flows.
3. Send `Authorization: Bearer <token>`, `Content-Type: application/json`, `Accept: application/json, text/event-stream`. Export `HABITINATOR_MCP_TOKEN` to keep secrets out of git.
4. Read the [server card](https://habitinator.app/.well-known/mcp/server-card.json) for versions and capabilities.

## Call order

- Start with `get_board_summary` for a cheap overview, then `get_snapshot` or `search_items` for item IDs.
- Item actions such as `toggle_item`, `habit_plus`, and `daily_complete_for_date` accept an explicit itemId GUID or a unique title.
- Dates accept `yyyy-MM-dd` and relative values `today`, `yesterday`, and `tomorrow`. Times use `HH:mm`. The server rejects bad values with `InvalidParams` that names the field and quotes what you sent. Fix the input and retry.
- `tags` accepts a comma-separated string or an array of strings. `"work,urgent"` and `["work", "urgent"]` store the same canonical value `work,urgent`, up to 500 chars. UI, REST, and MCP share one parser and one format.
- Checklist fields `checklistJson` and `json` on `set_checklist` accept a JSON string or a native array of `{text, isDone}` objects.
- Habits move with `habit_plus` and `habit_minus`. Past dailies complete with `daily_complete_for_date`. Today's items toggle with `toggle_item` or `batch_toggle_items`.
- `batch_create_items` returns one result per item. Valid items persist even when siblings fail, so only fix and retry the failed entries.
- Reuse `eventId` on retry for `log_activity` and `log_timer_session` so the server logs the event once. For creates, pass `idempotencyKey` and reuse it on retry.
- A near-duplicate title fails with `Similar item exists: '<title>'`. The check compares items in the same section. It ignores case and accents. A contains match or a trigram score at or above 0.8 triggers the warning. Retry with `force: true` to create anyway. `allowDuplicate: true` still works.
- Pass `dryRun: true` on `create_item` or `batch_create_items` to validate everything without persisting.
- `delete_item`, `import_data`, and mass `bulk_archive` ask for elicitation confirmation. Without elicitation support, pass `confirmToken` set to the item ID or `CONFIRM`. The error message states the exact retry.
- `get_due_digest` returns overdue, due-today, and due-tomorrow todos in one batch. Each todo appears at most once. Pass `snoozedIds` to exclude snoozed items. The digest honors notification settings. It stays empty when daily reminders are off, when in-app messages are off, or during quiet hours.

## Reference

- Full tool, prompt, and resource reference: [the MCP reference on GitHub](https://github.com/kadato/Habitinator/blob/main/docs/mcp.md).

## Machine-checked tool schemas

The markers below mirror docs/mcp.md. `McpSchemaConformanceTests` parses both files and fails the build on drift. Each entry is `name:type` with JSON Schema base types.

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
