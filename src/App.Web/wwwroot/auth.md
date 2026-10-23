# Habitinator agent auth

> How an AI agent or external client registers and calls Habitinator on behalf of a user.

Habitinator board data is private. Every MCP and REST call needs the user's own credential. There is no shared API key.

## 1. Register or sign in

- To register, call `POST /api/auth/register` with `{"email","password"}`.
- To sign in, call `POST /api/auth/login` with the same body. The response has `accessToken`, a short-lived JWT.
- To sign in with a browser, open `https://habitinator.app/auth/login` and use the session cookie.

## 2. Mint a personal MCP token

Call `POST /api/account/mcp-token` with the login JWT or the session cookie:

```bash
curl -s -X POST https://habitinator.app/api/account/mcp-token \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"days":90,"label":"Claude Desktop"}'
```

The server clamps `days` to 1-365. The response has `token`, `expiresAtUtc`, `endpoint`, and `days`. The Settings panel mints the same token without curl. Open **Settings**, **AI assistant**, **External AI access (MCP)**. The panel also tests the token and copies client configs.

## 3. Call MCP

Call `POST https://habitinator.app/mcp`. The endpoint uses Streamable HTTP in stateless mode. Send these headers: `Authorization: Bearer <mcp-token>`, `Content-Type: application/json`, and `Accept: application/json, text/event-stream`. Read the [server card](https://habitinator.app/.well-known/mcp/server-card.json) for versions and capabilities. Read the full tool, prompt, and resource reference in [the MCP reference on GitHub](https://github.com/kadato/Habitinator/blob/main/docs/mcp.md).

```bash
curl -s -X POST https://habitinator.app/mcp \
  -H "Authorization: Bearer $MCP_TOKEN" \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
```

## 4. Rules

- Tokens belong to one user. Never share them, commit them, or send them to another service.
- Destructive tools (`delete_item`, `delete_tag`, `import_data`, mass `bulk_archive`) ask for elicitation confirmation. Clients without elicitation pass `confirmToken`.
- Tokens expire. When one expires, generate a new one with the same label. There is no refresh flow.
- Revocation takes effect immediately. Revoke one token or all tokens in the Settings panel. The panel lives under **Settings**, **AI assistant**, **External AI access (MCP)**. The API equivalents are `DELETE /api/account/mcp-tokens/{id}` and `POST /api/account/mcp-tokens/revoke-all`. Revoked tokens fail with 401. Tokens minted before the registry existed stay valid until they expire.
