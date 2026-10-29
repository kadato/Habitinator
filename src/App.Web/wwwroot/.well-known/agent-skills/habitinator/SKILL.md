---
name: habitinator
description: Use the Habitinator MCP server to read and update a user's habits, dailies, todos, activity, and focus timer. Prefer get_board_summary first, then get_snapshot or search_items for IDs.
---

# Habitinator MCP skill

Habitinator exposes boards over Streamable HTTP MCP at `POST https://habitinator.app/mcp`. Board data is private and needs a Bearer personal MCP token.

## When to use this

Use it when the user asks about their habits, dailies, todos, streaks, activity, or focus sessions in Habitinator.

## Setup

1. Get a token in the app. Open **Settings**, **AI assistant**, **External AI access (MCP)** and select **Generate MCP token**. Tokens support Full, TrackingOnly, and ReadOnly scopes. Without the app, call `POST /api/account/mcp-token` with a login JWT.
2. Send `Authorization: Bearer <token>`, `Content-Type: application/json`, `Accept: application/json, text/event-stream`. For developer agents like OpenCode, Hermes, and Claude Code, export `HABITINATOR_MCP_TOKEN` to keep secrets out of git.
3. Read the [server card](https://habitinator.app/.well-known/mcp/server-card.json) for versions and capabilities. Read the [auth guide](https://habitinator.app/auth.md) for registration and token minting.

## Call order

- Start with `get_board_summary` for a cheap overview, then `get_snapshot` or `search_items` for item IDs.
- Item actions such as `toggle_item`, `habit_plus`, and `daily_complete_for_date` accept an explicit itemId GUID or a unique title.
- Dates accept `yyyy-MM-dd` and relative values `today`, `yesterday`, and `tomorrow`. Times use `HH:mm`. The server rejects bad values with `InvalidParams`. Fix the input and retry.
- Habits move with `habit_plus` and `habit_minus`. Past dailies complete with `daily_complete_for_date`. Today's items toggle with `toggle_item` or `batch_toggle_items`.
- Reuse `eventId` on retry for `log_activity` and `log_timer_session` so the server logs the event once.
- `delete_item`, `delete_tag`, `import_data`, and mass `bulk_archive` ask for elicitation confirmation. Without elicitation support, pass `confirmToken` set to the item ID or `CONFIRM`.

## Reference

- Full tool, prompt, and resource reference: [the MCP reference on GitHub](https://github.com/kadato/Habitinator/blob/main/docs/mcp.md).
