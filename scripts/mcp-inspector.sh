#!/usr/bin/env bash
set -euo pipefail

# Habitinator MCP Inspector launcher
# Runs the official @modelcontextprotocol/inspector against a running Habitinator web instance.

BASE_URL="${HABITINATOR_URL:-http://localhost:5000}"
EMAIL="${HABITINATOR_EMAIL:-demo@habitinator.local}"
PASSWORD="${HABITINATOR_PASSWORD:-Habitinator123!}"

echo "==> Habitinator MCP Inspector Launcher"
echo "==> Target: $BASE_URL/mcp"

# Check if npx is available
if ! command -v npx >/dev/null 2>&1; then
    echo "Error: 'npx' is required to launch the MCP inspector. Please install Node.js."
    exit 1
fi

# Request login token
echo "==> Authenticating as $EMAIL..."
TOKEN=$(curl -s -f -X POST "$BASE_URL/api/auth/login" \
    -H "Content-Type: application/json" \
    -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}" | grep -o '"accessToken":"[^"]*' | cut -d'"' -f4 || true)

if [ -z "$TOKEN" ]; then
    echo "Notice: Could not authenticate automatically via /api/auth/login."
    echo "You can provide an Authorization: Bearer <token> header directly in the Inspector UI."
    npx @modelcontextprotocol/inspector "$BASE_URL/mcp"
else
    echo "==> Authentication successful."
    echo "==> Starting MCP Inspector connected to $BASE_URL/mcp..."
    npx @modelcontextprotocol/inspector "$BASE_URL/mcp" --header "Authorization: Bearer $TOKEN"
fi
