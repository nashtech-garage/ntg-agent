#!/usr/bin/env bash
# desc: Start the Aspire AppHost and auto-open the dashboard in your browser
#
# Runs `dotnet run --project NTG.Agent.AppHost`, streams its output normally, and
# opens the Aspire dashboard automatically once its (tokenized) login URL appears.
# The URL is always printed prominently as a fallback in case auto-open is blocked.
# Ctrl-C stops the AppHost cleanly. Extra args are forwarded to `dotnet run`.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

# Matches the Aspire "Login to the dashboard at https://localhost:17050/login?t=..." URL.
URL_REGEX='https?://[^[:space:]]*/login\?t=[^[:space:]]*'

opened=""
# `dotnet run | while read` keeps both in the terminal's process group, so Ctrl-C
# reaches dotnet and it shuts down cleanly. `set -o pipefail` is inherited above.
dotnet run --project NTG.Agent.AppHost "$@" 2>&1 | while IFS= read -r line; do
  printf '%s\n' "$line"
  if [ -z "$opened" ]; then
    url="$(printf '%s' "$line" | grep -oE "$URL_REGEX" | head -n 1 || true)"
    if [ -n "$url" ]; then
      opened="1"
      printf '\n  ┌──────────────────────────────────────────────────────────\n'
      printf '  │ Aspire dashboard: %s\n' "$url"
      printf '  │ (opening in your browser; click the URL above if it does not)\n'
      printf '  └──────────────────────────────────────────────────────────\n\n'
      # Open in the background so we never block streaming; ignore failures.
      (open "$url" >/dev/null 2>&1 &) || true
    fi
  fi
done
