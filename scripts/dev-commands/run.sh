#!/usr/bin/env bash
# desc: Start the local Aspire AppHost and auto-open the dashboard
#
# Refreshes the AppHost user-secrets from the existing local .env, ensures the
# local LightRAG services are running, then starts the AppHost in local mode.
# Ctrl-C stops the AppHost cleanly. Extra args are forwarded to `dotnet run`.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

# Global tools are not always on PATH in a fresh shell, and the first-run
# installer normally installs dotnet-ef before reaching this command.
export PATH="$PATH:$HOME/.dotnet/tools"
if ! dotnet ef --version >/dev/null 2>&1; then
  printf '%s\n' 'dotnet-ef is not available; installing it now.'
  dotnet tool install --global dotnet-ef
fi

"$ROOT/scripts/init-apphost-user-secrets.sh" < /dev/null

docker compose \
  -f "$ROOT/deploy/lightrag-local/docker-compose.yml" \
  up -d

# Matches the Aspire "Login to the dashboard at https://localhost:17050/login?t=..." URL.
URL_REGEX='https?://[^[:space:]]*/login\?t=[^[:space:]]*'

opened=""
# Keep the launcher in the foreground while allowing Ctrl-C to reach dotnet.
"$ROOT/start-local-lightrag.sh" "$@" 2>&1 | while IFS= read -r line; do
  printf '%s\n' "$line"
  if [ -z "$opened" ]; then
    url="$(printf '%s' "$line" | grep -oE "$URL_REGEX" | head -n 1 || true)"
    if [ -n "$url" ]; then
      opened="1"
      printf '\n  Aspire dashboard: %s\n' "$url"
      printf '  (opening in your browser; click the URL above if it does not)\n\n'
      (open "$url" >/dev/null 2>&1 &) || true
    fi
  fi
done
