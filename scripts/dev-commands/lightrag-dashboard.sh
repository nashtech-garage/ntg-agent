#!/usr/bin/env bash
# desc: Pick a running agent and open its LightRAG WebUI in the browser
#
# Lists the running per-agent LightRAG containers (lightrag-agent-{guid}) as a
# numbered menu, prints the API key for the WebUI login, and opens the chosen
# agent's WebUI through the gateway in your default browser. Set
# LIGHTRAG_WEBUI_SCHEME, LIGHTRAG_WEBUI_HOST_SUFFIX, and LIGHTRAG_WEBUI_PORT
# when using a remote gateway with wildcard DNS.
# Everything is discovered from Docker + user-secrets — no app/API changes.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

APPHOST_CSPROJ="src/NTG.Agent.AppHost/NTG.Agent.AppHost.csproj"
WEBUI_SCHEME="${LIGHTRAG_WEBUI_SCHEME:-http}"
WEBUI_HOST_SUFFIX="${LIGHTRAG_WEBUI_HOST_SUFFIX:-.localhost}"
WEBUI_PORT="${LIGHTRAG_WEBUI_PORT:-8080}"

# --- find running per-agent LightRAG containers (portable: no mapfile) ---
CONTAINERS=()
while IFS= read -r line; do
  [ -n "$line" ] && CONTAINERS+=("$line")
done < <(docker ps --filter "name=lightrag-agent-" --format '{{.Names}}' 2>/dev/null | sort)
if [ "${#CONTAINERS[@]}" -eq 0 ]; then
  echo "No running LightRAG agent containers found."
  echo "Start the stack first:  ./ntg run   (or: dotnet run --project src/NTG.Agent.AppHost)"
  exit 0
fi

# --- best-effort: guid -> agent name, from the SQL Server Agents table ---
# Stored as 'guid|name' lines (no associative arrays — keeps bash 3.2 happy).
AGENT_ROWS=""
build_name_map() {
  local mssql sa_pw sqlcmd
  mssql="$(docker ps --filter "name=sqlserver" --format '{{.Names}}' 2>/dev/null | head -n 1 || true)"
  [ -n "$mssql" ] || return 0
  sa_pw="$(dotnet user-secrets list --project "$APPHOST_CSPROJ" 2>/dev/null \
            | sed -n 's/^Parameters:sql-sa-password = //p')"
  [ -n "$sa_pw" ] || sa_pw='Admin123_Strong!'
  sqlcmd='/opt/mssql-tools18/bin/sqlcmd'
  # No -i on these execs: they don't read stdin, and -i would steal the script's
  # stdin (breaking the numbered prompt's `read`).
  docker exec "$mssql" test -x "$sqlcmd" 2>/dev/null || sqlcmd='/opt/mssql-tools/bin/sqlcmd'
  AGENT_ROWS="$(docker exec "$mssql" "$sqlcmd" -S localhost -U sa -P "$sa_pw" -C -d NTGAgent \
            -h -1 -W -s '|' -Q \
            "SET NOCOUNT ON; SELECT LOWER(CONVERT(varchar(36), Id)), Name FROM Agents;" \
            2>/dev/null | grep '|' || true)"
}
build_name_map

name_of() {  # echo the agent name for a guid, or nothing if unknown
  [ -n "$AGENT_ROWS" ] || return 0
  printf '%s\n' "$AGENT_ROWS" | awk -F'|' -v g="$1" '$1==g{print $2; exit}'
}

# --- LightRAG API key for the WebUI login (best-effort) ---
API_KEY="$(dotnet user-secrets list --project "$APPHOST_CSPROJ" 2>/dev/null \
            | sed -n 's/^Parameters:lightrag-api-key = //p')"

# --- build the numbered list (label + URL) ---
labels=()
urls=()
for c in "${CONTAINERS[@]}"; do
  guid="${c#lightrag-agent-}"
  label="$(name_of "$guid")"
  [ -n "$label" ] || label="$guid"
  labels+=("$label")
  urls+=("${WEBUI_SCHEME}://agent-${guid}${WEBUI_HOST_SUFFIX}:${WEBUI_PORT}/webui/")
done

if [ "${#urls[@]}" -eq 0 ]; then
  echo "No running LightRAG agent containers found."
  exit 0
fi

echo "Running LightRAG dashboards:"
for i in "${!labels[@]}"; do
  printf '  %d) %-28s %s\n' "$((i + 1))" "${labels[$i]}" "${urls[$i]}"
done
echo
if [ -n "$API_KEY" ]; then
  printf 'API key (paste at the WebUI login): %s\n' "$API_KEY"
else
  echo "API key: (not found in user-secrets — run ./scripts/init-apphost-user-secrets.sh)"
fi
printf 'Choose a number to open (q to quit): '

read -r choice
case "$choice" in
  q | Q | "") exit 0 ;;
esac
if ! [[ "$choice" =~ ^[0-9]+$ ]] || [ "$choice" -lt 1 ] || [ "$choice" -gt "${#urls[@]}" ]; then
  echo "Invalid choice: $choice" >&2
  exit 1
fi

url="${urls[$((choice - 1))]}"
echo "Opening ${labels[$((choice - 1))]} -> $url"
open "$url"
