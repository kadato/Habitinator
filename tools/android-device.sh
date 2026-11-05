#!/usr/bin/env bash
# Deploy the MAUI Android app to an attached physical device against the local
# Aspire backend. One command starts Aspire, forwards the port, points the app
# at the port, and runs the build.
#
# Usage: ./tools/android-device.sh
# Env:   HABITINATOR_HTTP_PORT (default 5050), ASPIRE_WAIT_SECONDS (default 600)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PORT="${HABITINATOR_HTTP_PORT:-5050}"
WAIT_SECONDS="${ASPIRE_WAIT_SECONDS:-600}"
ANDROID_CONFIG="$ROOT/src/App.MAUI/appsettings.Android.json"
ANDROID_CONFIG_BAK="$ANDROID_CONFIG.dev-bak"
ASPIRE_LOG="${TMPDIR:-/tmp}/habitinator-aspire.log"
ASPIRE_PID_FILE="${TMPDIR:-/tmp}/habitinator-aspire.pid"

cleanup() {
    if [[ -f "$ANDROID_CONFIG_BAK" ]]; then
        mv -f "$ANDROID_CONFIG_BAK" "$ANDROID_CONFIG"
    fi
}
trap cleanup EXIT INT TERM

die() {
    echo "android-device: ERROR: $*" >&2
    exit 1
}

command -v adb >/dev/null || die "adb not found. Install Android platform-tools first."
command -v dotnet >/dev/null || die "dotnet not found."
command -v python3 >/dev/null || die "python3 not found (needed to patch the Android config)."
command -v curl >/dev/null || die "curl not found."

# A device must be attached and authorized. An unauthorized status means the RSA prompt
# on the phone has not been accepted yet.
DEVICES="$(adb devices)"
if ! grep -q $'\tdevice$' <<<"$DEVICES"; then
    if grep -q $'\tunauthorized$' <<<"$DEVICES"; then
        die "Phone is attached but unauthorized. Accept the USB debugging prompt on the phone, then retry."
    fi
    die "No authorized device found. Attach the phone (usbipd on WSL, or USB/Wi-Fi debugging) and retry."
fi

export HABITINATOR_HTTP_PORT="$PORT"

backend_up() {
    curl -fsS --max-time 3 "http://127.0.0.1:${PORT}/health" >/dev/null 2>&1
}

if backend_up; then
    echo "android-device: backend already healthy on :${PORT}."
else
    echo "android-device: starting Aspire on :${PORT} (log: ${ASPIRE_LOG})..."
    (cd "$ROOT" && nohup dotnet run --project src/AppHost/AppHost.csproj >"$ASPIRE_LOG" 2>&1 & echo $! >"$ASPIRE_PID_FILE")
    deadline=$((SECONDS + WAIT_SECONDS))
    while ! backend_up; do
        if ((SECONDS >= deadline)); then
            echo "android-device: backend did not become healthy in ${WAIT_SECONDS}s. Log tail:" >&2
            tail -30 "$ASPIRE_LOG" >&2 || true
            die "start Aspire manually and check the log at ${ASPIRE_LOG}."
        fi
        sleep 3
    done
    echo "android-device: backend healthy."
    dashboard_proto="https?"
    grep -m1 -oE "${dashboard_proto}://(localhost|127\.0\.0\.1):[0-9]+[^ ]*" "$ASPIRE_LOG" | head -1 | sed 's/^/android-device: dashboard: /' || true # NOSONAR: local dev Aspire dashboard link
fi

# The phone's localhost:<port> reaches this machine's localhost:<port>.
adb reverse "tcp:${PORT}" "tcp:${PORT}"

# Point the embedded Android config at the forwarded port for this build only.
# Emulator builds keep the committed 10.0.2.2 default. The trap restores it.
cp -f "$ANDROID_CONFIG" "$ANDROID_CONFIG_BAK"
python3 - "$ANDROID_CONFIG" "$PORT" <<'EOF'
import json, sys
path, port = sys.argv[1], sys.argv[2]
with open(path) as f:
    config = json.load(f)
config["Api"]["BaseUrl"] = f"http://localhost:{port}"
with open(path, "w") as f:
    json.dump(config, f, indent=2)
    f.write("\n")
EOF

echo "android-device: building and deploying to the attached device..."
dotnet build "$ROOT/src/App.MAUI/App.MAUI.csproj" -t:Run -f net11.0-android
echo "android-device: done. The app on the phone talks to the local backend on :${PORT}."
