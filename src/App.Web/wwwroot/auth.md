# Habitinator agent auth

> How an AI agent or external client registers and calls Habitinator on behalf of a user.

Habitinator board data is private. Every MCP and REST call needs the user's own credential. There is no shared API key.

## 1. Connect with zero-key OAuth 2.1

Start here for interactive use. For interactive clients such as Claude Desktop, Cursor, VS Code, and OpenCode:
- Add `https://habitinator.app/mcp` to your client configuration without an authorization header.
- The client discovers OAuth metadata at `/.well-known/oauth-protected-resource` and `/.well-known/oauth-authorization-server`.
- The client prompts you to approve access in your browser at `/oauth/authorize`.
- The client exchanges the authorization code using PKCE for an access token and refresh token, rotating them automatically.

## 2. Register or sign in manually

- To register, call `POST /api/auth/register` with `{"email","password"}`.
- To sign in, call `POST /api/auth/login` with the same body. The response has `accessToken`, a short-lived JWT.
- To sign in with a browser, open `https://habitinator.app/auth/login` and use the session cookie.

## 3. Approve a headless device with OAuth

Terminal agents that cannot open a browser use the RFC 8628 device flow:

1. The agent calls `POST /oauth/device/authorize` with `{"client_id":"opencode","scope":"full"}` and shows you the `verification_uri` plus the `user_code`, for example `ABCD-EFGH`.
2. You open the URI in your browser, sign in, enter the code, and approve once. Codes expire in 10 minutes. Each code works once. The server binds each code to the requesting `client_id`.
3. The agent polls `POST /oauth/token` with `{"grant_type":"urn:ietf:params:oauth:grant-type:device_code","device_code":"...","client_id":"opencode"}` every `interval` seconds. While you have not approved, the server answers `authorization_pending`.
4. The agent calls MCP with `Authorization: Bearer <access_token>` and rotates via the `refresh_token` grant. Refresh tokens rotate on use.

```bash
curl -s -X POST https://habitinator.app/oauth/device/authorize \
  -H 'Content-Type: application/json' \
  -d '{"client_id":"opencode","scope":"full"}'
```

## 4. Mint a personal MCP token

Call `POST /api/account/mcp-token` with the login JWT or the session cookie:

```bash
curl -s -X POST https://habitinator.app/api/account/mcp-token \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"days":90,"label":"Claude Desktop"}'
```

The server clamps `days` to 1-365. The response has `token`, `expiresAtUtc`, `endpoint`, and `days`. Open **Settings**, **AI assistant**, **External AI access (MCP)** to mint the same token without curl.

## 5. Call MCP

Call `POST https://habitinator.app/mcp`. Send these headers: `Authorization: Bearer <mcp-token>`, `Content-Type: application/json`, and `Accept: application/json, text/event-stream`. Read the [server card](https://habitinator.app/.well-known/mcp/server-card.json) for versions and capabilities. The full tool reference lives in [the MCP reference on GitHub](https://github.com/kadato/Habitinator/blob/main/docs/mcp.md).

```bash
curl -s -X POST https://habitinator.app/mcp \
  -H "Authorization: Bearer $MCP_TOKEN" \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
```

## 6. Rules

- Tokens belong to one user. Never share them, commit them, or send them to another service.
- Destructive tools (`delete_item`, `import_data`, mass `bulk_archive`) ask for elicitation confirmation. Clients without elicitation pass `confirmToken`. (`delete_tag` only removes a label and applies immediately.)
- Tokens expire. When one expires, generate a new one with the same label. There is no refresh flow.
- Revocation takes effect immediately. Revoke tokens in the Settings panel or with `DELETE /api/account/mcp-tokens/{id}` and `POST /api/account/mcp-tokens/revoke-all`. Revoked tokens fail with 401. Tokens minted before the registry existed stay valid until they expire.
