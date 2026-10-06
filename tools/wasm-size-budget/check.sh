#!/usr/bin/env bash
# Fails when the published Blazor _framework payload exceeds budget.txt.
# Usage: check.sh <framework-dir>
# The framework dir of a client publish is <publish-dir>/wwwroot/_framework.
# Current payload is ~37.2 MB against a 40 MB budget. Raise the budget only
# with a reason recorded in the PR, never to silence the gate.
set -euo pipefail

dir="${1:?usage: check.sh <framework-dir>}"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
budget="$(tr -d ' \r\n' < "$script_dir/budget.txt")"
actual="$(du -sb "$dir" | cut -f1)"

echo "WASM _framework bytes: $actual (budget $budget)"
if [ "$actual" -gt "$budget" ]; then
  echo "WASM payload exceeds budget by $((actual - budget)) bytes."
  echo "Largest 10 artifacts:"
  find "$dir" -type f -exec du -b {} + | sort -rn | head -10
  exit 1
fi
